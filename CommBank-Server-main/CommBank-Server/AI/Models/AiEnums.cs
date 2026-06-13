namespace CommBank.AI.Models;

/// <summary>Coarse risk tier derived from a continuous risk score.</summary>
public enum RiskBand
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>The action the platform should take for a scored transaction.</summary>
public enum RiskDecision
{
    /// <summary>Allow the transaction to proceed.</summary>
    Approve,

    /// <summary>Allow but flag for asynchronous manual/AML review.</summary>
    Review,

    /// <summary>Reject/hold the transaction.</summary>
    Block
}

/// <summary>
/// Behaviour when the scoring pipeline itself fails (not when it returns a high score).
/// Fail-open favours availability; fail-closed favours safety. The orchestrator selects
/// per-transaction based on monetary exposure.
/// </summary>
public enum RiskFailureMode
{
    Open,
    Closed
}

/// <summary>Backing implementation for the financial assistant.</summary>
public enum AssistantProvider
{
    /// <summary>Deterministic, in-process intent engine over the caller's own data. No external calls.</summary>
    Local,
    Anthropic,
    OpenAi
}

/// <summary>Qualitative outlook for a savings goal.</summary>
public enum ForecastOutlook
{
    Achieved,
    OnTrack,
    AtRisk,
    OffTrack
}
