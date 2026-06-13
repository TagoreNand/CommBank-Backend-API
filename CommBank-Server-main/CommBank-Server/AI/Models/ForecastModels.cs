namespace CommBank.AI.Models;

/// <summary>Forward-looking projection for a single savings goal.</summary>
public sealed class GoalForecast
{
    public string? GoalId { get; init; }
    public string? GoalName { get; init; }

    public double CurrentBalance { get; init; }
    public double TargetAmount { get; init; }
    public DateTime TargetDate { get; init; }

    /// <summary>CurrentBalance / TargetAmount, clamped to [0,1].</summary>
    public double CompletionRatio { get; init; }

    /// <summary>Observed average monthly net contribution over the lookback window.</summary>
    public double MonthlyContributionRunRate { get; init; }

    /// <summary>Contribution per month required to hit the target by the target date.</summary>
    public double RequiredMonthlyContribution { get; init; }

    /// <summary>Projected completion date at the current run-rate, or null if not progressing.</summary>
    public DateTime? ProjectedCompletionDate { get; init; }

    /// <summary>Probability the goal is met by its target date, in [0,1].</summary>
    public double OnTrackProbability { get; init; }

    public ForecastOutlook Outlook { get; init; }

    public string Summary { get; init; } = "";

    public string ModelVersion { get; init; } = "0.0.0";

    public DateTime EvaluatedAtUtc { get; init; } = DateTime.UtcNow;
}

/// <summary>A statistically unusual spend detected in a user's transaction history.</summary>
public sealed record SpendingAnomaly(
    string? TransactionId,
    double Amount,
    double ZScore,
    string Description,
    DateTime OccurredAt);

/// <summary>Aggregate spending insights for a user over an evaluation window.</summary>
public sealed class SpendingInsights
{
    public string? UserId { get; init; }

    public double TotalDebits { get; init; }
    public double TotalCredits { get; init; }
    public double NetCashFlow { get; init; }
    public double AverageDebit { get; init; }

    public IReadOnlyList<SpendingAnomaly> Anomalies { get; init; } = Array.Empty<SpendingAnomaly>();

    /// <summary>Total debit value grouped by tag id (best-effort; un-tagged grouped under "untagged").</summary>
    public IReadOnlyDictionary<string, double> SpendByTag { get; init; } =
        new Dictionary<string, double>();

    public string ModelVersion { get; init; } = "0.0.0";

    public DateTime EvaluatedAtUtc { get; init; } = DateTime.UtcNow;
}
