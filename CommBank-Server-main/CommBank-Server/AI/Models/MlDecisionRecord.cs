using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CommBank.AI.Models;

/// <summary>
/// Append-only audit record of a single automated model decision. Persisted to its own collection
/// so that every fraud/AML/categorization decision is independently reconstructable for regulators.
/// Stores a hash of the feature inputs rather than raw PII, never the underlying account data.
/// </summary>
public sealed class MlDecisionRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>"risk" | "categorization" | "forecast" | "assistant".</summary>
    public string DecisionType { get; set; } = "";

    public string ModelId { get; set; } = "";

    public string ModelVersion { get; set; } = "";

    /// <summary>The entity the decision was about (e.g. transaction or goal id).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string? SubjectId { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? UserId { get; set; }

    public string? IdempotencyKey { get; set; }

    public double Score { get; set; }

    public string Decision { get; set; } = "";

    /// <summary>SHA-256 of the ordered feature vector; lets auditors prove inputs without storing PII.</summary>
    public string? InputsHash { get; set; }

    /// <summary>Serialized reason codes/contributions captured at decision time.</summary>
    public string? ReasonsJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
