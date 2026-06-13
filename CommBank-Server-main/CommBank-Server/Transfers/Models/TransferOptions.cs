namespace CommBank.Transfers.Models;

/// <summary>Configuration for the fund-transfer workflow, bound from the "Transfers" section.</summary>
public sealed class TransferOptions
{
    public const string SectionName = "Transfers";

    /// <summary>Maximum re-attempts when an optimistic-concurrency or transient transaction conflict occurs.</summary>
    public int MaxConcurrencyRetries { get; set; } = 3;

    /// <summary>When false, a transfer that would overdraw the source account is rejected.</summary>
    public bool AllowOverdraft { get; set; } = false;

    /// <summary>When true, every transfer is risk-scored before commit and blocked on a Block decision.</summary>
    public bool RiskCheckEnabled { get; set; } = true;

    /// <summary>Transfers at/above this amount require a step-up (MFA) token carrying the acr=mfa claim.</summary>
    public decimal StepUpAmountThreshold { get; set; } = 10000m;

    public string TransfersCollection { get; set; } = "Transfers";

    public string IdempotencyCollection { get; set; } = "TransferIdempotency";
}
