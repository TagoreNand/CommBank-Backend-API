using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace CommBank.Auth;

/// <summary>
/// A persisted refresh token. Only the SHA-256 <see cref="TokenHash"/> is stored — never the raw value —
/// so a database leak cannot be replayed. Rotation links each token to its successor; presenting a
/// already-revoked token signals reuse/compromise.
/// </summary>
public sealed class RefreshToken
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string TokenHash { get; set; } = "";

    [BsonRepresentation(BsonType.ObjectId)]
    public string? UserId { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAtUtc { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedByIp { get; set; }

    [BsonIgnore]
    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}
