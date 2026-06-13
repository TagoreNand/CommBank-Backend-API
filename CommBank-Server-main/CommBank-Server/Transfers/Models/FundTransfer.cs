using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CommBank.Transfers.Models;

public enum TransferStatus
{
    Pending,
    Completed,
    Blocked,
    Failed
}

/// <summary>
/// Immutable audit record of a fund transfer, written inside the same transaction as the balance
/// mutations so the trail can never diverge from the ledger. Amount is persisted as Decimal128.
/// </summary>
public sealed class FundTransfer
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? SourceAccountId { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? DestinationAccountId { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Amount { get; set; }

    public string? Description { get; set; }

    public string Status { get; set; } = nameof(TransferStatus.Pending);

    /// <summary>Client idempotency key; a unique index on this guarantees at-most-once execution.</summary>
    public string? IdempotencyKey { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? DebitTransactionId { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? CreditTransactionId { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? UserId { get; set; }

    public double? RiskScore { get; set; }

    public string? RiskDecision { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }
}
