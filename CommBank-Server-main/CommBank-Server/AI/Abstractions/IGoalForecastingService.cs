using CommBank.AI.Models;
using CommBank.Models;

namespace CommBank.AI.Abstractions;

/// <summary>
/// Port for goal completion forecasting and spending analytics. Pure functions over data the caller
/// has already loaded; no I/O, so they are trivially unit-testable and free of failure modes.
/// </summary>
public interface IGoalForecastingService
{
    GoalForecast Forecast(Goal goal, IReadOnlyList<Transaction> goalTransactions, DateTime nowUtc);

    SpendingInsights AnalyzeSpending(
        string? userId,
        IReadOnlyList<Transaction> userTransactions,
        DateTime nowUtc);
}
