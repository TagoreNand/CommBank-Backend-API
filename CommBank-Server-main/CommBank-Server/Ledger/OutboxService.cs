using System.Text.Json;
using CommBank.Ledger.Models;
using MongoDB.Driver;

namespace CommBank.Ledger;

/// <summary>Writes outbox messages inside the caller's transaction (transactional outbox pattern).</summary>
public interface IOutboxService
{
    Task EnqueueAsync(IClientSessionHandle session, string type, object payload, CancellationToken cancellationToken = default);
}

public sealed class OutboxService : IOutboxService
{
    private readonly IMongoCollection<OutboxMessage> _outbox;

    public OutboxService(IMongoDatabase database) =>
        _outbox = database.GetCollection<OutboxMessage>("Outbox");

    public Task EnqueueAsync(IClientSessionHandle session, string type, object payload, CancellationToken cancellationToken = default) =>
        _outbox.InsertOneAsync(
            session,
            new OutboxMessage
            {
                Type = type,
                Payload = JsonSerializer.Serialize(payload),
                Status = nameof(OutboxStatus.Pending)
            },
            options: null,
            cancellationToken);
}
