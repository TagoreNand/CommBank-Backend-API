using MongoDB.Bson;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace CommBank.Models;

public class Account
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public long? Number { get; set; }

    public string? Name { get; set; }

    public double Balance { get; set; } = 0;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    [BsonRepresentation(BsonType.String)]
    public AccountType AccountType { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public List<string>? TransactionIds { get; set; }

    /// <summary>
    /// Optimistic-concurrency token. Incremented on every balance mutation; a conditional update that
    /// no longer matches the expected version reveals a concurrent write (lost-update protection).
    /// Legacy documents that predate this field are treated as version 0.
    /// </summary>
    public long Version { get; set; }
}