namespace CommBank.AI.Models;

/// <summary>Recognised natural-language intents the assistant can answer from the user's own data.</summary>
public enum AssistantIntent
{
    Unknown,
    SpendByCategory,
    SpendByPeriod,
    AccountBalance,
    GoalProgress,
    LargestTransactions
}

/// <summary>An inbound assistant request, always scoped to a single authenticated user.</summary>
public sealed class AssistantQuery
{
    /// <summary>The user whose data may be queried. Authorization is enforced by the caller.</summary>
    public string? UserId { get; init; }

    public string Question { get; init; } = "";
}

/// <summary>A grounded assistant answer plus the structured evidence used to derive it.</summary>
public sealed class AssistantResponse
{
    public string Answer { get; init; } = "";

    public AssistantIntent Intent { get; init; }

    /// <summary>Confidence the intent was understood, in [0,1].</summary>
    public double Confidence { get; init; }

    /// <summary>Concrete figures used to build the answer, for transparency/audit.</summary>
    public IReadOnlyList<string> DataPoints { get; init; } = Array.Empty<string>();

    public string Provider { get; init; } = "local";

    /// <summary>True when the answer is derived solely from the user's own records (no hallucination surface).</summary>
    public bool Grounded { get; init; } = true;

    public DateTime EvaluatedAtUtc { get; init; } = DateTime.UtcNow;

    public static AssistantResponse Unhandled(string message) => new()
    {
        Answer = message,
        Intent = AssistantIntent.Unknown,
        Confidence = 0d,
        Grounded = true
    };
}
