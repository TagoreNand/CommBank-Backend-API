using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.AI.Services;

namespace CommBank.AI.DependencyInjection;

/// <summary>
/// Single composition root for the intelligence module. Binds <see cref="AiOptions"/> and registers
/// every port against its in-process adapter. Swapping a model (e.g. an ML.NET or remote risk scorer,
/// or a real LLM client) is a one-line change here — no caller is affected.
/// </summary>
public static class IntelligenceServiceCollectionExtensions
{
    public static IServiceCollection AddCommBankIntelligence(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));

        // Default in-process adapters. Replace any single line to upgrade a capability.
        services.AddSingleton<IRiskScoringService, HeuristicRiskScoringService>();
        services.AddSingleton<ICategorizationService, RuleBasedCategorizationService>();
        services.AddSingleton<IGoalForecastingService, GoalForecastingService>();
        services.AddSingleton<ILanguageModelClient, NullLanguageModelClient>();
        services.AddSingleton<IMlDecisionAuditService, MongoMlDecisionAuditService>();
        services.AddSingleton<IFinancialAssistantService, FinancialAssistantService>();

        // Application services.
        services.AddSingleton<IRiskAwareTransactionOrchestrator, RiskAwareTransactionOrchestrator>();

        return services;
    }
}
