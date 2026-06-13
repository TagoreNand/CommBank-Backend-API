using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CommBank.Ledger.Models;

public enum OutboxStatus
{
    Pending,
    Processed,
    Failed
}

/// <summary>
/// Transactional outbox record. Written in the SAME transaction as the business change, then relayed by
/// the <c>OutboxProcessor</c>. This closes the dual-write gap: an event is published if and only if the
/// originating transaction committed.
/// </summary>
public sealed class OutboxMessage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>Event type, e.g. "transfer.completed".</summary>
    public string Type { get; set; } = "";

    /// <summary>Serialized event payload (JSON).</summary>
    public string Payload { get; set; } = "";

    public string Status { get; set; } = nameof(OutboxStatus.Pending);

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAtUtc { get; set; }
}
