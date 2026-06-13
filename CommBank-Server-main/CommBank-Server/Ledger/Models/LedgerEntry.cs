using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CommBank.Ledger.Models;

public enum LedgerDirection
{
    Debit,
    Credit
}

/// <summary>
/// A single posting in the double-entry ledger. Postings are grouped by <see cref="JournalId"/>; within a
/// journal the total of all debits must equal the total of all credits. Amounts are Decimal128.
/// </summary>
public sealed class LedgerEntry
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>Groups the balanced legs of one accounting event.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string? JournalId { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? AccountId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public LedgerDirection Direction { get; set; }

    [BsonRepresentation(BsonType.Decimal128)]
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "AUD";

    [BsonRepresentation(BsonType.ObjectId)]
    public string? TransferId { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
