using MongoDB.Bson;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace CommBank.Models;

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Email { get; set; }

    // Hidden from all JSON output so the BCrypt hash never leaves the API; still persisted to Mongo.
    // Registration binds the raw password via RegisterInput, not this model.
    [JsonIgnore]
    public string? Password { get; set; }

    /// <summary>Authorization roles (e.g. "Customer", "Admin"). Server-assigned; never set by clients.</summary>
    public List<string>? Roles { get; set; }

    /// <summary>True once the user has verified a TOTP enrollment.</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>Base32 TOTP shared secret. Never serialized to clients.</summary>
    [JsonIgnore]
    public string? MfaSecret { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string>? AccountIds { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string>? GoalIds { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string>? TransactionIds { get; set; }
}
