namespace CommBank.Transfers.Models;

/// <summary>The outcome of a transfer, returned to the caller and safe to serialize.</summary>
public sealed class TransferResult
{
    public string? TransferId { get; init; }

    public string Status { get; init; } = "";

    public decimal Amount { get; init; }

    public string? SourceAccountId { get; init; }

    public string? DestinationAccountId { get; init; }

    public decimal SourceBalanceAfter { get; init; }

    public decimal DestinationBalanceAfter { get; init; }

    public string? DebitTransactionId { get; init; }

    public string? CreditTransactionId { get; init; }

    /// <summary>True when this result was replayed from a prior execution of the same idempotency key.</summary>
    public bool Idempotent { get; init; }

    public double? RiskScore { get; init; }

    public string? RiskDecision { get; init; }

    public DateTime CompletedAtUtc { get; init; }
}
