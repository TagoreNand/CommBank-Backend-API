using CommBank.Models;

namespace CommBank.AI.Models;

/// <summary>A single explainable factor that contributed to a risk score.</summary>
/// <param name="Code">Stable machine code, e.g. "AMOUNT_ZSCORE".</param>
/// <param name="Description">Human-readable explanation for analysts/audit.</param>
/// <param name="Contribution">Points this factor added to the raw score (>= 0).</param>
public sealed record RiskReason(string Code, string Description, double Contribution);

/// <summary>
/// Immutable result of scoring a single transaction. This is the contract returned by every
/// <see cref="Abstractions.IRiskScoringService"/> implementation (heuristic, ML.NET, or remote),
/// so the model can be swapped without touching callers.
/// </summary>
public sealed class RiskAssessment
{
    public string? TransactionId { get; init; }

    public string? UserId { get; init; }

    /// <summary>Continuous risk score in the inclusive range [0, 1].</summary>
    public double Score { get; init; }

    public RiskBand Band { get; init; }

    public RiskDecision Decision { get; init; }

    /// <summary>Ordered, explainable factors behind the score (highest contribution first).</summary>
    public IReadOnlyList<RiskReason> Reasons { get; init; } = Array.Empty<RiskReason>();

    public string ModelId { get; init; } = "commbank.risk.heuristic";

    public string ModelVersion { get; init; } = "0.0.0";

    /// <summary>
    /// Idempotency key of the originating request. Scoring the same logical event twice
    /// (e.g. a network retry) must yield the same assessment and a single audit record.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    public DateTime EvaluatedAtUtc { get; init; } = DateTime.UtcNow;

    public long EvaluationLatencyMs { get; init; }

    public bool Flagged => Decision != RiskDecision.Approve;

    public static RiskAssessment Approved(string? transactionId, string? userId, string modelVersion) => new()
    {
        TransactionId = transactionId,
        UserId = userId,
        Score = 0d,
        Band = RiskBand.Low,
        Decision = RiskDecision.Approve,
        ModelVersion = modelVersion
    };
}

/// <summary>
/// Pure inputs required to score a transaction. The caller assembles this (fetching history,
/// account state) so the scorer stays a deterministic, side-effect-free, unit-testable function.
/// </summary>
public sealed class RiskScoringContext
{
    /// <summary>The debited/credited account, when known. Used for balance-drain features.</summary>
    public Account? Account { get; init; }

    /// <summary>Recent transactions for the same user, newest-first or any order; used for velocity and z-score.</summary>
    public IReadOnlyList<Transaction> RecentUserTransactions { get; init; } = Array.Empty<Transaction>();

    /// <summary>Evaluation clock (UTC). Injected for deterministic testing.</summary>
    public DateTime NowUtc { get; init; } = DateTime.UtcNow;

    /// <summary>Idempotency key flowed through to the resulting assessment and audit record.</summary>
    public string? IdempotencyKey { get; init; }
}
