using System.Diagnostics;
using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommBank.AI.Services;

/// <summary>
/// In-process, deterministic, fully-explainable risk scorer. Each fired signal contributes weighted
/// points and an audit-grade <see cref="RiskReason"/>; the total is squashed to [0,1] and banded.
/// Designed as a drop-in for a future ML.NET/remote model behind <see cref="IRiskScoringService"/>:
/// callers depend only on the port, never on this class.
/// </summary>
public sealed class HeuristicRiskScoringService : IRiskScoringService
{
    private const string ModelIdentifier = "commbank.risk.heuristic";

    private readonly AiOptions _options;
    private readonly ILogger<HeuristicRiskScoringService> _logger;

    public HeuristicRiskScoringService(IOptions<AiOptions> options, ILogger<HeuristicRiskScoringService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<RiskAssessment> ScoreAsync(
        Transaction transaction,
        RiskScoringContext context,
        CancellationToken cancellationToken = default)
    {
        RiskScoringOptions opt = _options.RiskScoring;
        var stopwatch = Stopwatch.StartNew();

        TransactionFeatures features = TransactionFeatureExtractor.Extract(transaction, context, opt);
        var reasons = new List<RiskReason>();
        double score = 0d;

        void Signal(string code, string description, double contribution)
        {
            if (contribution <= 0d)
            {
                return;
            }

            reasons.Add(new RiskReason(code, description, Math.Round(contribution, 4)));
            score += contribution;
        }

        // 1. Absolute magnitude.
        if (features.Amount >= opt.CriticalAmountThreshold)
        {
            Signal("AMOUNT_CRITICAL", $"Amount {features.Amount:N2} is at/above the critical threshold {opt.CriticalAmountThreshold:N2}.", 0.35);
        }
        else if (features.Amount >= opt.HighAmountThreshold)
        {
            Signal("AMOUNT_HIGH", $"Amount {features.Amount:N2} is at/above the high threshold {opt.HighAmountThreshold:N2}.", 0.20);
        }

        // 2. Personal anomaly (z-score vs the user's own recent spend).
        if (features.AmountZScore > 0d)
        {
            double contribution = Math.Min(0.35d, features.AmountZScore / 6d);
            Signal("AMOUNT_ZSCORE", $"Amount is {features.AmountZScore:N1} standard deviations above this user's recent average.", contribution);
        }

        // 3. Velocity (burst of activity).
        if (features.VelocityCount >= opt.VelocityCountThreshold)
        {
            Signal("VELOCITY", $"{features.VelocityCount} transactions within {opt.VelocityWindowMinutes} minutes (threshold {opt.VelocityCountThreshold}).", 0.25);
        }

        // 4. Balance drain.
        if (features.BalanceDrainRatio >= opt.BalanceDrainRatio)
        {
            Signal("BALANCE_DRAIN", $"Outflow consumes {features.BalanceDrainRatio:P0} of the available balance.", 0.20);
        }

        // 5. Structuring / round-tripping.
        if (features.IsStructuringPattern)
        {
            Signal("STRUCTURING", $"Amount sits just below the {opt.HighAmountThreshold:N0} reporting threshold (possible structuring).", 0.20);
        }
        else if (features.IsRoundLargeAmount)
        {
            Signal("ROUND_LARGE", "Large, perfectly round amount.", 0.10);
        }

        // 6. Rapid repeat — duplicate/retry without an idempotency key.
        if (features.IsRapidRepeat)
        {
            Signal("RAPID_REPEAT", "Identical amount and type seen within 2 minutes (possible duplicate or unguarded retry).", 0.30);
        }

        // 7. High-value transfer.
        if (features.IsLargeTransfer)
        {
            Signal("LARGE_TRANSFER", "High-value outbound transfer.", 0.10);
        }

        // 8. Unusual hours.
        if (features.IsOddHour)
        {
            Signal("ODD_HOUR", $"Activity during unusual hours ({opt.OddHourStartInclusive:00}:00-{opt.OddHourEndExclusive:00}:00).", 0.05);
        }

        score = Math.Clamp(score, 0d, 1d);
        (RiskBand band, RiskDecision decision) = Classify(score, opt);

        var assessment = new RiskAssessment
        {
            TransactionId = transaction.Id,
            UserId = transaction.UserId,
            Score = Math.Round(score, 4),
            Band = band,
            Decision = decision,
            Reasons = reasons.OrderByDescending(r => r.Contribution).ToList(),
            ModelId = ModelIdentifier,
            ModelVersion = opt.ModelVersion,
            IdempotencyKey = context.IdempotencyKey,
            EvaluatedAtUtc = context.NowUtc,
            EvaluationLatencyMs = stopwatch.ElapsedMilliseconds
        };

        if (assessment.Flagged)
        {
            // Structured, PII-free observability event.
            _logger.LogInformation(
                "Risk {Decision} band={Band} score={Score} txn={TransactionId} user={UserId} reasons={ReasonCodes}",
                assessment.Decision, assessment.Band, assessment.Score, transaction.Id, transaction.UserId,
                string.Join(',', assessment.Reasons.Select(r => r.Code)));
        }

        return Task.FromResult(assessment);
    }

    /// <summary>Hash of the exact feature vector used, for the immutable audit record.</summary>
    public string ComputeInputsHash(Transaction transaction, RiskScoringContext context) =>
        TransactionFeatureExtractor.ComputeHash(
            TransactionFeatureExtractor.Extract(transaction, context, _options.RiskScoring));

    private static (RiskBand Band, RiskDecision Decision) Classify(double score, RiskScoringOptions opt)
    {
        if (score >= opt.BlockThreshold)
        {
            return (RiskBand.Critical, RiskDecision.Block);
        }

        if (score >= opt.ReviewThreshold)
        {
            return (RiskBand.High, RiskDecision.Review);
        }

        if (score >= opt.MediumThreshold)
        {
            return (RiskBand.Medium, RiskDecision.Approve);
        }

        return (RiskBand.Low, RiskDecision.Approve);
    }
}
