using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.Models;
using CommBank.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommBank.AI.Services;

/// <summary>
/// Coordinates the risk-aware money path: idempotency check -> score -> decide -> persist or block ->
/// audit. Scoring failures degrade per policy (fail-open for low value, fail-closed above a threshold)
/// so the system stays both available and safe. The transaction is only written when the decision is
/// not <see cref="RiskDecision.Block"/>.
/// </summary>
public sealed class RiskAwareTransactionOrchestrator : IRiskAwareTransactionOrchestrator
{
    private const string DecisionType = "risk";

    private readonly IRiskScoringService _scorer;
    private readonly IMlDecisionAuditService _audit;
    private readonly ITransactionsService _transactions;
    private readonly IAccountsService _accounts;
    private readonly IUsersService _users;
    private readonly AiOptions _options;
    private readonly ILogger<RiskAwareTransactionOrchestrator> _logger;

    public RiskAwareTransactionOrchestrator(
        IRiskScoringService scorer,
        IMlDecisionAuditService audit,
        ITransactionsService transactions,
        IAccountsService accounts,
        IUsersService users,
        IOptions<AiOptions> options,
        ILogger<RiskAwareTransactionOrchestrator> logger)
    {
        _scorer = scorer;
        _audit = audit;
        _transactions = transactions;
        _accounts = accounts;
        _users = users;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TransactionDecision> CreateAsync(
        Transaction transaction,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // 1. Idempotency: replay a prior decision for the same key without re-inserting.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            MlDecisionRecord? prior = await _audit.FindByIdempotencyKeyAsync(idempotencyKey!, DecisionType, cancellationToken);
            if (prior is not null)
            {
                _logger.LogInformation("Idempotent replay for key {IdempotencyKey}", idempotencyKey);
                return Replay(prior);
            }
        }

        // 2. Score (guarded). Scoring should never throw, but the money-path must survive it if it does.
        RiskAssessment assessment;
        try
        {
            RiskScoringContext context = await BuildContextAsync(transaction, idempotencyKey, cancellationToken);
            assessment = await _scorer.ScoreAsync(transaction, context, cancellationToken);
        }
        catch (Exception ex)
        {
            bool failClosed = _options.RiskScoring.FailureMode == RiskFailureMode.Closed
                              || Math.Abs(transaction.Amount) >= _options.RiskScoring.FailClosedAmountThreshold;

            _logger.LogError(ex, "Risk scoring failed for user {UserId}; failing {Mode}", transaction.UserId, failClosed ? "CLOSED" : "OPEN");

            if (failClosed)
            {
                RiskAssessment blocked = ErrorAssessment(transaction, idempotencyKey, RiskDecision.Block, "Scoring pipeline error; failed closed for safety.");
                await _audit.RecordAsync(BuildRecord(blocked, transaction), cancellationToken);
                return new TransactionDecision { Assessment = blocked, Persisted = false, Status = "blocked_scoring_error" };
            }

            assessment = ErrorAssessment(transaction, idempotencyKey, RiskDecision.Approve, "Scoring pipeline error; failed open (low value).");
        }

        // 3. Block decision: never persist, but always audit.
        if (assessment.Decision == RiskDecision.Block)
        {
            await _audit.RecordAsync(BuildRecord(assessment, transaction), cancellationToken);
            _logger.LogWarning("Blocked transaction for user {UserId} score {Score}", transaction.UserId, assessment.Score);
            return new TransactionDecision { Assessment = assessment, Persisted = false, Status = "blocked" };
        }

        // 4. Approve / review: persist the transaction, then audit with the assigned id.
        await _transactions.CreateAsync(transaction);

        var stamped = new RiskAssessment
        {
            TransactionId = transaction.Id,
            UserId = assessment.UserId,
            Score = assessment.Score,
            Band = assessment.Band,
            Decision = assessment.Decision,
            Reasons = assessment.Reasons,
            ModelId = assessment.ModelId,
            ModelVersion = assessment.ModelVersion,
            IdempotencyKey = assessment.IdempotencyKey,
            EvaluatedAtUtc = assessment.EvaluatedAtUtc,
            EvaluationLatencyMs = assessment.EvaluationLatencyMs
        };

        await _audit.RecordAsync(BuildRecord(stamped, transaction), cancellationToken);

        string status = stamped.Decision == RiskDecision.Review ? "created_flagged_for_review" : "created";
        return new TransactionDecision { Assessment = stamped, Persisted = true, Status = status };
    }

    private async Task<RiskScoringContext> BuildContextAsync(Transaction transaction, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var recent = new List<Transaction>();
        if (!string.IsNullOrWhiteSpace(transaction.UserId))
        {
            recent = await _transactions.GetForUserAsync(transaction.UserId) ?? new List<Transaction>();
        }

        Account? account = await ResolveAccountAsync(transaction.UserId);

        return new RiskScoringContext
        {
            Account = account,
            RecentUserTransactions = recent,
            NowUtc = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey
        };
    }

    // Transactions carry no direct account reference; use the user's account only when unambiguous.
    private async Task<Account?> ResolveAccountAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        User? user = await _users.GetAsync(userId);
        if (user?.AccountIds is { Count: 1 } && !string.IsNullOrWhiteSpace(user.AccountIds[0]))
        {
            return await _accounts.GetAsync(user.AccountIds[0]);
        }

        return null;
    }

    private TransactionDecision Replay(MlDecisionRecord prior)
    {
        RiskDecision decision = Enum.TryParse(prior.Decision, out RiskDecision parsed) ? parsed : RiskDecision.Approve;

        var assessment = new RiskAssessment
        {
            TransactionId = prior.SubjectId,
            UserId = prior.UserId,
            Score = prior.Score,
            Band = BandFromScore(prior.Score),
            Decision = decision,
            ModelId = prior.ModelId,
            ModelVersion = prior.ModelVersion,
            IdempotencyKey = prior.IdempotencyKey,
            EvaluatedAtUtc = prior.CreatedAtUtc
        };

        return new TransactionDecision
        {
            Assessment = assessment,
            Persisted = decision != RiskDecision.Block,
            Status = "idempotent_replay",
            Idempotent = true
        };
    }

    private RiskBand BandFromScore(double score)
    {
        RiskScoringOptions o = _options.RiskScoring;
        if (score >= o.BlockThreshold) return RiskBand.Critical;
        if (score >= o.ReviewThreshold) return RiskBand.High;
        if (score >= o.MediumThreshold) return RiskBand.Medium;
        return RiskBand.Low;
    }

    private RiskAssessment ErrorAssessment(Transaction transaction, string? idempotencyKey, RiskDecision decision, string note) => new()
    {
        TransactionId = transaction.Id,
        UserId = transaction.UserId,
        Score = decision == RiskDecision.Block ? 1d : 0d,
        Band = decision == RiskDecision.Block ? RiskBand.Critical : RiskBand.Low,
        Decision = decision,
        Reasons = new[] { new RiskReason("SCORING_ERROR", note, 0d) },
        ModelId = "commbank.risk.orchestrator",
        ModelVersion = _options.RiskScoring.ModelVersion,
        IdempotencyKey = idempotencyKey,
        EvaluatedAtUtc = DateTime.UtcNow
    };

    private static MlDecisionRecord BuildRecord(RiskAssessment assessment, Transaction transaction) => new()
    {
        DecisionType = DecisionType,
        ModelId = assessment.ModelId,
        ModelVersion = assessment.ModelVersion,
        SubjectId = transaction.Id,
        UserId = assessment.UserId ?? transaction.UserId,
        IdempotencyKey = assessment.IdempotencyKey,
        Score = assessment.Score,
        Decision = assessment.Decision.ToString(),
        InputsHash = HashInputs(transaction, assessment.IdempotencyKey),
        ReasonsJson = JsonSerializer.Serialize(assessment.Reasons)
    };

    private static string HashInputs(Transaction transaction, string? idempotencyKey)
    {
        string canonical = $"{Math.Abs(transaction.Amount):F2}|{(int)transaction.TransactionType}|{transaction.UserId}|{idempotencyKey}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
