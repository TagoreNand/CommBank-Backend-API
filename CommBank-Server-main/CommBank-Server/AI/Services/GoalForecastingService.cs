using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.Models;
using Microsoft.Extensions.Options;

namespace CommBank.AI.Services;

/// <summary>
/// Deterministic goal-completion forecasting and spending analytics. Uses a contribution run-rate
/// projected against the goal's deadline, plus z-score anomaly detection over the user's debits.
/// Pure compute (no I/O), so it is side-effect-free and exhaustively unit-testable.
/// </summary>
public sealed class GoalForecastingService : IGoalForecastingService
{
    private const double DaysPerMonth = 30.4375d;
    private const double MinMonths = 0.0329d; // ~1 day, guards divide-by-zero near the deadline

    private readonly AiOptions _options;

    public GoalForecastingService(IOptions<AiOptions> options) => _options = options.Value;

    public GoalForecast Forecast(Goal goal, IReadOnlyList<Transaction> goalTransactions, DateTime nowUtc)
    {
        ForecastingOptions opt = _options.Forecasting;

        double current = goal.Balance;
        double target = goal.TargetAmount;
        double completion = target > 0d
            ? Math.Clamp(current / target, 0d, 1d)
            : (current > 0d ? 1d : 0d);

        IReadOnlyList<Transaction> txns = goalTransactions ?? Array.Empty<Transaction>();
        DateTime windowStart = nowUtc.AddDays(-opt.LookbackDays);
        List<Transaction> windowTxns = txns
            .Where(t => AsUtc(t.DateTime) >= windowStart && AsUtc(t.DateTime) <= nowUtc)
            .ToList();

        double netContribution = windowTxns.Sum(SignedContribution);

        double observedDays = opt.LookbackDays;
        if (windowTxns.Count > 0)
        {
            DateTime earliest = windowTxns.Min(t => AsUtc(t.DateTime));
            observedDays = Math.Max(1d, (nowUtc - earliest).TotalDays);
        }

        double monthlyRunRate = netContribution / Math.Max(observedDays / DaysPerMonth, MinMonths);
        if (monthlyRunRate < 0d)
        {
            monthlyRunRate = 0d; // a net-withdrawing goal is not progressing
        }

        double remaining = Math.Max(0d, target - current);
        double monthsToTarget = (goal.TargetDate - nowUtc).TotalDays / DaysPerMonth;
        double requiredMonthly = monthsToTarget > MinMonths ? remaining / monthsToTarget : remaining;

        DateTime? projectedCompletion = null;
        if (remaining <= 0d)
        {
            projectedCompletion = nowUtc;
        }
        else if (monthlyRunRate > 0d)
        {
            projectedCompletion = nowUtc.AddDays(remaining / monthlyRunRate * DaysPerMonth);
        }

        double onTrack;
        if (completion >= 1d || requiredMonthly <= 0d)
        {
            onTrack = 1d;
        }
        else
        {
            onTrack = Math.Clamp(monthlyRunRate / requiredMonthly / 1.25d, 0d, 0.98d);
        }

        ForecastOutlook outlook;
        if (completion >= 1d)
        {
            outlook = ForecastOutlook.Achieved;
        }
        else if (monthsToTarget <= 0d)
        {
            outlook = ForecastOutlook.OffTrack;
        }
        else if (onTrack >= 0.70d)
        {
            outlook = ForecastOutlook.OnTrack;
        }
        else if (onTrack >= 0.40d)
        {
            outlook = ForecastOutlook.AtRisk;
        }
        else
        {
            outlook = ForecastOutlook.OffTrack;
        }

        return new GoalForecast
        {
            GoalId = goal.Id,
            GoalName = goal.Name,
            CurrentBalance = Math.Round(current, 2),
            TargetAmount = target,
            TargetDate = goal.TargetDate,
            CompletionRatio = Math.Round(completion, 4),
            MonthlyContributionRunRate = Math.Round(monthlyRunRate, 2),
            RequiredMonthlyContribution = Math.Round(requiredMonthly, 2),
            ProjectedCompletionDate = projectedCompletion,
            OnTrackProbability = Math.Round(onTrack, 3),
            Outlook = outlook,
            Summary = BuildSummary(goal, completion, monthlyRunRate, requiredMonthly, projectedCompletion, outlook),
            ModelVersion = opt.ModelVersion
        };
    }

    public SpendingInsights AnalyzeSpending(string? userId, IReadOnlyList<Transaction> userTransactions, DateTime nowUtc)
    {
        ForecastingOptions opt = _options.Forecasting;
        IReadOnlyList<Transaction> txns = userTransactions ?? Array.Empty<Transaction>();

        List<Transaction> debitTxns = txns
            .Where(t => t.TransactionType is TransactionType.Debit or TransactionType.Transfer)
            .ToList();
        List<double> debitAmounts = debitTxns.Select(t => Math.Abs(t.Amount)).ToList();

        double totalDebits = debitAmounts.Sum();
        double totalCredits = txns
            .Where(t => t.TransactionType == TransactionType.Credit)
            .Sum(t => Math.Abs(t.Amount));
        double averageDebit = debitAmounts.Count > 0 ? debitAmounts.Average() : 0d;

        var anomalies = new List<SpendingAnomaly>();
        if (debitAmounts.Count >= 3)
        {
            double mean = debitAmounts.Average();
            double variance = debitAmounts.Sum(a => (a - mean) * (a - mean)) / debitAmounts.Count;
            double stdDev = Math.Sqrt(variance);

            if (stdDev > 0.0001d)
            {
                foreach (Transaction t in debitTxns)
                {
                    double amount = Math.Abs(t.Amount);
                    double z = (amount - mean) / stdDev;
                    if (z >= opt.AnomalyZScoreThreshold)
                    {
                        anomalies.Add(new SpendingAnomaly(
                            t.Id,
                            Math.Round(amount, 2),
                            Math.Round(z, 2),
                            $"Debit of {amount:N2} is {z:N1} standard deviations above the average of {mean:N2}.",
                            AsUtc(t.DateTime)));
                    }
                }
            }
        }

        var spendByTag = new Dictionary<string, double>();
        foreach (Transaction t in debitTxns)
        {
            string key = t.TagIds is { Length: > 0 } && t.TagIds[0] is not null ? t.TagIds[0]! : "untagged";
            spendByTag[key] = spendByTag.GetValueOrDefault(key) + Math.Abs(t.Amount);
        }

        return new SpendingInsights
        {
            UserId = userId,
            TotalDebits = Math.Round(totalDebits, 2),
            TotalCredits = Math.Round(totalCredits, 2),
            NetCashFlow = Math.Round(totalCredits - totalDebits, 2),
            AverageDebit = Math.Round(averageDebit, 2),
            Anomalies = anomalies.OrderByDescending(a => a.ZScore).ToList(),
            SpendByTag = spendByTag,
            ModelVersion = opt.ModelVersion
        };
    }

    private static double SignedContribution(Transaction t) => t.TransactionType switch
    {
        TransactionType.Credit => Math.Abs(t.Amount),
        TransactionType.Transfer => Math.Abs(t.Amount),
        TransactionType.Debit => -Math.Abs(t.Amount),
        _ => 0d
    };

    private static string BuildSummary(
        Goal goal,
        double completion,
        double runRate,
        double required,
        DateTime? projected,
        ForecastOutlook outlook)
    {
        string name = string.IsNullOrWhiteSpace(goal.Name) ? "This goal" : goal.Name!;

        return outlook switch
        {
            ForecastOutlook.Achieved =>
                $"{name} is fully funded ({completion:P0} of target).",
            ForecastOutlook.OnTrack =>
                $"{name} is on track at {completion:P0}. Current pace {runRate:N0}/mo vs {required:N0}/mo required" +
                (projected is { } p ? $"; projected completion {p:yyyy-MM-dd}." : "."),
            ForecastOutlook.AtRisk =>
                $"{name} is at risk at {completion:P0}. Pace {runRate:N0}/mo is below the {required:N0}/mo needed to hit the target date.",
            _ =>
                $"{name} is off track at {completion:P0}. It needs {required:N0}/mo" +
                (runRate > 0d ? $" but is only receiving {runRate:N0}/mo." : " and is not currently receiving contributions.")
        };
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
