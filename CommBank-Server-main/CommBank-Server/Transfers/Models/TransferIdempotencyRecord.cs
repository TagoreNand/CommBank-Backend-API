using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CommBank.Transfers.Models;

/// <summary>
/// Idempotency ledger entry. The idempotency key is the document <c>_id</c>, which gives a free unique
/// index: a concurrent duplicate insert fails with a duplicate-key error, which the service treats as a
/// signal to replay the original result rather than execute the transfer twice.
/// </summary>
public sealed class TransferIdempotencyRecord
{
    [BsonId]
    public string Id { get; set; } = "";

    [BsonRepresentation(BsonType.ObjectId)]
    public string? TransferId { get; set; }

    public string Status { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
