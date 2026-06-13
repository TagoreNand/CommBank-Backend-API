namespace CommBank.AI.Models;

/// <summary>
/// Strongly-typed configuration for the intelligence module, bound from the "Ai" configuration
/// section. Contains only non-secret tuning parameters; any provider API keys are resolved from
/// the secret providers (env vars / user-secrets), never from committed appsettings.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public RiskScoringOptions RiskScoring { get; set; } = new();
    public CategorizationOptions Categorization { get; set; } = new();
    public ForecastingOptions Forecasting { get; set; } = new();
    public AssistantOptions Assistant { get; set; } = new();
    public AuditOptions Audit { get; set; } = new();
}

public sealed class RiskScoringOptions
{
    public bool Enabled { get; set; } = true;
    public string ModelVersion { get; set; } = "2026.06.1";

    /// <summary>Score at/above which a transaction is banded Medium (approved but surfaced).</summary>
    public double MediumThreshold { get; set; } = 0.40;

    /// <summary>Score at/above which a transaction is flagged for review (High band).</summary>
    public double ReviewThreshold { get; set; } = 0.65;

    /// <summary>Score at/above which a transaction is blocked (Critical band).</summary>
    public double BlockThreshold { get; set; } = 0.85;

    public double HighAmountThreshold { get; set; } = 10_000d;
    public double CriticalAmountThreshold { get; set; } = 50_000d;

    public int VelocityWindowMinutes { get; set; } = 10;
    public int VelocityCountThreshold { get; set; } = 5;

    /// <summary>Debit-to-balance ratio above which a balance-drain signal fires.</summary>
    public double BalanceDrainRatio { get; set; } = 0.90;

    /// <summary>Local hours [Start, End) treated as unusual activity windows.</summary>
    public int OddHourStartInclusive { get; set; } = 0;
    public int OddHourEndExclusive { get; set; } = 5;

    /// <summary>Default behaviour if the pipeline throws; overridden to Closed above the fail-closed amount.</summary>
    public RiskFailureMode FailureMode { get; set; } = RiskFailureMode.Open;

    /// <summary>Transactions at/above this amount fail CLOSED (blocked) when scoring errors.</summary>
    public double FailClosedAmountThreshold { get; set; } = 10_000d;
}

public sealed class CategorizationOptions
{
    public bool Enabled { get; set; } = true;
    public string ModelVersion { get; set; } = "2026.06.1";

    /// <summary>Minimum confidence [0,1] required before a tag suggestion is auto-applied.</summary>
    public double MinConfidence { get; set; } = 0.55;

    /// <summary>Maximum number of tag suggestions returned per transaction.</summary>
    public int MaxSuggestions { get; set; } = 3;
}

public sealed class ForecastingOptions
{
    public bool Enabled { get; set; } = true;
    public string ModelVersion { get; set; } = "2026.06.1";

    /// <summary>Window of history used to compute contribution run-rate.</summary>
    public int LookbackDays { get; set; } = 90;

    /// <summary>Z-score threshold above which a spend is reported as an anomaly.</summary>
    public double AnomalyZScoreThreshold { get; set; } = 3.0;
}

public sealed class AssistantOptions
{
    public bool Enabled { get; set; } = true;
    public AssistantProvider Provider { get; set; } = AssistantProvider.Local;

    /// <summary>Cap on transactions loaded into the assistant's working context.</summary>
    public int MaxHistoryTransactions { get; set; } = 500;

    /// <summary>Hosted-model id (only used when Provider != Local).</summary>
    public string Model { get; set; } = "";

    /// <summary>Timeout for any hosted-LLM call.</summary>
    public int RequestTimeoutSeconds { get; set; } = 15;
}

public sealed class AuditOptions
{
    public bool Enabled { get; set; } = true;
    public string CollectionName { get; set; } = "MlDecisions";
}
