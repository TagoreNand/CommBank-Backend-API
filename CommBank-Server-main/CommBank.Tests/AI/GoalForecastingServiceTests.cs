using CommBank.AI.Models;
using CommBank.AI.Services;
using CommBank.Models;
using Microsoft.Extensions.Options;

namespace CommBank.Tests.AI;

public class GoalForecastingServiceTests
{
    private static GoalForecastingService NewService(AiOptions? options = null) =>
        new(Options.Create(options ?? new AiOptions()));

    [Fact]
    public void SteadyContributions_GoalIsOnTrack()
    {
        var now = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var goal = new Goal
        {
            Id = "g1",
            Name = "Car",
            TargetAmount = 12000,
            Balance = 6000,
            TargetDate = now.AddMonths(6),
            Created = now.AddMonths(-6)
        };
        var transactions = new List<Transaction>
        {
            Credit("1", "g1", 1000, now.AddDays(-30)),
            Credit("2", "g1", 1000, now.AddDays(-60)),
            Credit("3", "g1", 1000, now.AddDays(-90))
        };

        GoalForecast forecast = NewService().Forecast(goal, transactions, now);

        Assert.Equal(ForecastOutlook.OnTrack, forecast.Outlook);
        Assert.InRange(forecast.CompletionRatio, 0.49d, 0.51d);
        Assert.True(forecast.RequiredMonthlyContribution > 0d);
        Assert.True(forecast.MonthlyContributionRunRate > 0d);
    }

    [Fact]
    public void LargeOutlierDebit_IsFlaggedAsAnomaly()
    {
        var now = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        var options = new AiOptions();
        options.Forecasting.AnomalyZScoreThreshold = 1.5d;

        var transactions = new List<Transaction>
        {
            Debit("1", 100, now.AddDays(-1)),
            Debit("2", 100, now.AddDays(-2)),
            Debit("3", 100, now.AddDays(-3)),
            Debit("4", 100, now.AddDays(-4)),
            Debit("5", 100, now.AddDays(-5)),
            Debit("6", 10000, now.AddDays(-6))
        };

        SpendingInsights insights = NewService(options).AnalyzeSpending("u1", transactions, now);

        Assert.NotEmpty(insights.Anomalies);
        Assert.Contains(insights.Anomalies, a => a.Amount == 10000d);
    }

    private static Transaction Credit(string id, string goalId, double amount, DateTime when) => new()
    {
        Id = id,
        GoalId = goalId,
        Amount = amount,
        TransactionType = TransactionType.Credit,
        DateTime = when,
        UserId = "u1"
    };

    private static Transaction Debit(string id, double amount, DateTime when) => new()
    {
        Id = id,
        Amount = amount,
        TransactionType = TransactionType.Debit,
        DateTime = when,
        UserId = "u1"
    };
}
