namespace CommBank.Ledger;

/// <summary>Composition root for the double-entry ledger and the transactional outbox.</summary>
public static class LedgerServiceCollectionExtensions
{
    public static IServiceCollection AddCommBankLedger(this IServiceCollection services)
    {
        services.AddSingleton<ILedgerService, LedgerService>();
        services.AddSingleton<IOutboxService, OutboxService>();

        // Default event publisher logs; swap for a Kafka/RabbitMQ adapter without touching callers.
        services.AddSingleton<IEventPublisher, LoggingEventPublisher>();

        // Background relay that publishes outbox messages.
        services.AddHostedService<OutboxProcessor>();

        return services;
    }
}
