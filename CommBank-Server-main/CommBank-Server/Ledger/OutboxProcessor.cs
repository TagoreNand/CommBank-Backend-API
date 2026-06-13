using CommBank.Ledger.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace CommBank.Ledger;

/// <summary>
/// Background relay for the transactional outbox. Polls for pending messages, dispatches each via
/// <see cref="IEventPublisher"/>, and marks it processed — or retries up to <see cref="MaxAttempts"/>
/// before parking it as Failed. Because messages are written in the same transaction as the business
/// change, this guarantees at-least-once publication of events that actually happened.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IMongoCollection<OutboxMessage> _outbox;
    private readonly IEventPublisher _publisher;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IMongoDatabase database, IEventPublisher publisher, ILogger<OutboxProcessor> logger)
    {
        _outbox = database.GetCollection<OutboxMessage>("Outbox");
        _publisher = publisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox poll failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        FilterDefinition<OutboxMessage> pending =
            Builders<OutboxMessage>.Filter.Eq(m => m.Status, nameof(OutboxStatus.Pending));

        List<OutboxMessage> batch = await _outbox
            .Find(pending)
            .SortBy(m => m.CreatedAtUtc)
            .Limit(50)
            .ToListAsync(cancellationToken);

        foreach (OutboxMessage message in batch)
        {
            try
            {
                await _publisher.PublishAsync(message.Type, message.Payload, cancellationToken);

                UpdateDefinition<OutboxMessage> done = Builders<OutboxMessage>.Update
                    .Set(m => m.Status, nameof(OutboxStatus.Processed))
                    .Set(m => m.ProcessedAtUtc, DateTime.UtcNow)
                    .Inc(m => m.Attempts, 1);

                await _outbox.UpdateOneAsync(m => m.Id == message.Id, done, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                bool exhausted = message.Attempts + 1 >= MaxAttempts;

                UpdateDefinition<OutboxMessage> outcome = Builders<OutboxMessage>.Update
                    .Set(m => m.Status, exhausted ? nameof(OutboxStatus.Failed) : nameof(OutboxStatus.Pending))
                    .Set(m => m.LastError, ex.Message)
                    .Inc(m => m.Attempts, 1);

                await _outbox.UpdateOneAsync(m => m.Id == message.Id, outcome, cancellationToken: cancellationToken);
                _logger.LogError(ex, "Failed to publish outbox message {MessageId} (attempt {Attempt}).", message.Id, message.Attempts + 1);
            }
        }
    }
}
