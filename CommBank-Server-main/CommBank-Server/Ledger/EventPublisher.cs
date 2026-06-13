using Microsoft.Extensions.Logging;

namespace CommBank.Ledger;

/// <summary>
/// Seam for publishing domain events to the outside world. The default implementation logs; a real adapter
/// (Kafka/RabbitMQ/Azure Service Bus) would implement this and be registered in its place.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(string type, string payload, CancellationToken cancellationToken = default);
}

/// <summary>Default publisher: writes the event to structured logs (stand-in for a real broker).</summary>
public sealed class LoggingEventPublisher : IEventPublisher
{
    private readonly ILogger<LoggingEventPublisher> _logger;

    public LoggingEventPublisher(ILogger<LoggingEventPublisher> logger) => _logger = logger;

    public Task PublishAsync(string type, string payload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Published domain event {EventType}: {Payload}", type, payload);
        return Task.CompletedTask;
    }
}
