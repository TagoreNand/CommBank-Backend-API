namespace CommBank.Services;

/// <summary>
/// Registers the Mongo-backed domain services by interface. Replaces the hand-rolled
/// <c>new XService(db)</c> + <c>AddSingleton(instance)</c> wiring in Program.cs with idiomatic,
/// constructor-injected registrations (the services resolve <see cref="MongoDB.Driver.IMongoDatabase"/>
/// from the container).
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddCommBankPersistence(this IServiceCollection services)
    {
        services.AddSingleton<IAccountsService, AccountsService>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IGoalsService, GoalsService>();
        services.AddSingleton<ITagsService, TagsService>();
        services.AddSingleton<ITransactionsService, TransactionsService>();
        services.AddSingleton<IUsersService, UsersService>();
        return services;
    }
}
