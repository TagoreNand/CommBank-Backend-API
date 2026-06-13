namespace CommBank.AI.Models;

/// <summary>Outcome of a risk-aware transaction create: the assessment plus what the platform did with it.</summary>
public sealed class TransactionDecision
{
    public RiskAssessment Assessment { get; init; } = new();

    /// <summary>True when the transaction was written to the store (approved or approved-for-review).</summary>
    public bool Persisted { get; init; }

    /// <summary>Machine-readable outcome: created | created_flagged_for_review | blocked | blocked_scoring_error | idempotent_replay.</summary>
    public string Status { get; init; } = "";

    /// <summary>True when this result was replayed from a prior decision for the same idempotency key.</summary>
    public bool Idempotent { get; init; }
}
