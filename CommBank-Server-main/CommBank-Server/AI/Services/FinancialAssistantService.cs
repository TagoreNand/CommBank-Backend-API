using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.Models;
using CommBank.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommBank.AI.Services;

/// <summary>
/// Natural-language financial assistant. By default it answers entirely from the requesting user's own
/// records via a deterministic intent engine (no external calls, no hallucination surface). When a real
/// <see cref="ILanguageModelClient"/> is configured, unrecognised questions fall back to it with a
/// PII-masked, aggregate-only context. Every answer is scoped to a single user id.
/// </summary>
public sealed class FinancialAssistantService : IFinancialAssistantService
{
    private static readonly IReadOnlyDictionary<string, string[]> Categories =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["food"]          = new[] { "food", "grocery", "groceries", "dining", "restaurant", "cafe", "coffee", "eat" },
            ["transport"]     = new[] { "transport", "fuel", "petrol", "uber", "taxi", "train", "bus" },
            ["entertainment"] = new[] { "entertainment", "netflix", "spotify", "movie", "game", "games" },
            ["shopping"]      = new[] { "shopping", "amazon", "clothes", "clothing", "retail" },
            ["utilities"]     = new[] { "utilities", "electricity", "internet", "water", "energy", "phone" },
            ["health"]        = new[] { "health", "pharmacy", "gym", "medical", "doctor", "dental" }
        };

    private readonly ITransactionsService _transactions;
    private readonly IGoalsService _goals;
    private readonly IAccountsService _accounts;
    private readonly IUsersService _users;
    private readonly IGoalForecastingService _forecasting;
    private readonly ILanguageModelClient _languageModel;
    private readonly AiOptions _options;
    private readonly ILogger<FinancialAssistantService> _logger;

    public FinancialAssistantService(
        ITransactionsService transactions,
        IGoalsService goals,
        IAccountsService accounts,
        IUsersService users,
        IGoalForecastingService forecasting,
        ILanguageModelClient languageModel,
        IOptions<AiOptions> options,
        ILogger<FinancialAssistantService> logger)
    {
        _transactions = transactions;
        _goals = goals;
        _accounts = accounts;
        _users = users;
        _forecasting = forecasting;
        _languageModel = languageModel;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AssistantResponse> AskAsync(AssistantQuery query, CancellationToken cancellationToken = default)
    {
        if (!_options.Assistant.Enabled)
        {
            return AssistantResponse.Unhandled("The financial assistant is currently disabled.");
        }

        if (string.IsNullOrWhiteSpace(query.UserId))
        {
            return AssistantResponse.Unhandled("An authenticated user is required to answer account questions.");
        }

        string question = (query.Question ?? string.Empty).ToLowerInvariant().Trim();
        if (question.Length == 0)
        {
            return AssistantResponse.Unhandled("Please ask about your balances, goals, or spending.");
        }

        AssistantIntent intent = DetectIntent(question);

        try
        {
            return intent switch
            {
                AssistantIntent.AccountBalance => await AnswerBalanceAsync(query.UserId!),
                AssistantIntent.GoalProgress => await AnswerGoalsAsync(query.UserId!),
                AssistantIntent.SpendByCategory => await AnswerSpendByCategoryAsync(query.UserId!, question),
                AssistantIntent.SpendByPeriod => await AnswerSpendByPeriodAsync(query.UserId!, question),
                AssistantIntent.LargestTransactions => await AnswerLargestAsync(query.UserId!),
                _ => await FallbackAsync(query.UserId!, query.Question ?? string.Empty, cancellationToken)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Assistant failed for user {UserId} intent {Intent}", query.UserId, intent);
            return AssistantResponse.Unhandled("I couldn't complete that request just now. Please try again.");
        }
    }

    private static AssistantIntent DetectIntent(string q)
    {
        if (q.Contains("balance") || q.Contains("how much do i have") || q.Contains("in my account"))
        {
            return AssistantIntent.AccountBalance;
        }

        if (q.Contains("goal") || q.Contains("saving") || q.Contains("on track"))
        {
            return AssistantIntent.GoalProgress;
        }

        if (q.Contains("largest") || q.Contains("biggest") || q.Contains("top "))
        {
            return AssistantIntent.LargestTransactions;
        }

        bool spend = q.Contains("spend") || q.Contains("spent") || q.Contains("how much");
        if (spend && TryDetectCategory(q, out _, out _))
        {
            return AssistantIntent.SpendByCategory;
        }

        return spend ? AssistantIntent.SpendByPeriod : AssistantIntent.Unknown;
    }

    private async Task<AssistantResponse> AnswerBalanceAsync(string userId)
    {
        List<Account> accounts = await LoadUserAccountsAsync(userId);
        if (accounts.Count == 0)
        {
            return Grounded("You don't have any linked accounts yet.", AssistantIntent.AccountBalance, 0.9d);
        }

        double total = accounts.Sum(a => a.Balance);
        var dataPoints = accounts
            .Select(a => $"{a.Name ?? "Account"}: {a.Balance:C}")
            .ToList();

        return Grounded(
            $"Your total balance across {accounts.Count} account(s) is {total:C}.",
            AssistantIntent.AccountBalance,
            0.9d,
            dataPoints);
    }

    private async Task<AssistantResponse> AnswerGoalsAsync(string userId)
    {
        List<Goal> goals = await _goals.GetForUserAsync(userId) ?? new List<Goal>();
        if (goals.Count == 0)
        {
            return Grounded("You don't have any savings goals set up yet.", AssistantIntent.GoalProgress, 0.9d);
        }

        List<Transaction> txns = await LoadUserTransactionsAsync(userId);
        DateTime now = DateTime.UtcNow;
        var lines = new List<string>();

        foreach (Goal goal in goals)
        {
            List<Transaction> goalTxns = txns.Where(t => t.GoalId is not null && t.GoalId == goal.Id).ToList();
            GoalForecast forecast = _forecasting.Forecast(goal, goalTxns, now);
            lines.Add($"{forecast.Outlook}: {forecast.Summary}");
        }

        return Grounded(
            $"You have {goals.Count} goal(s). " + string.Join(" ", lines),
            AssistantIntent.GoalProgress,
            0.9d,
            lines);
    }

    private async Task<AssistantResponse> AnswerSpendByCategoryAsync(string userId, string question)
    {
        if (!TryDetectCategory(question, out string category, out string[] keywords))
        {
            return await AnswerSpendByPeriodAsync(userId, question);
        }

        List<Transaction> txns = await LoadUserTransactionsAsync(userId);
        (DateTime start, string label) = DetectPeriod(question, DateTime.UtcNow);

        double total = txns
            .Where(t => t.TransactionType is TransactionType.Debit or TransactionType.Transfer)
            .Where(t => AsUtc(t.DateTime) >= start)
            .Where(t => MatchesCategory(t.Description, keywords))
            .Sum(t => Math.Abs(t.Amount));

        string answer = total > 0d
            ? $"You spent {total:C} on {category} in {label}."
            : $"You have no recorded {category} spending in {label}.";

        return Grounded(answer, AssistantIntent.SpendByCategory, 0.8d);
    }

    private async Task<AssistantResponse> AnswerSpendByPeriodAsync(string userId, string question)
    {
        List<Transaction> txns = await LoadUserTransactionsAsync(userId);
        (DateTime start, string label) = DetectPeriod(question, DateTime.UtcNow);

        double total = txns
            .Where(t => t.TransactionType is TransactionType.Debit or TransactionType.Transfer)
            .Where(t => AsUtc(t.DateTime) >= start)
            .Sum(t => Math.Abs(t.Amount));

        return Grounded($"Your total spending in {label} was {total:C}.", AssistantIntent.SpendByPeriod, 0.75d);
    }

    private async Task<AssistantResponse> AnswerLargestAsync(string userId)
    {
        List<Transaction> txns = await LoadUserTransactionsAsync(userId);
        List<Transaction> top = txns
            .Where(t => t.TransactionType is TransactionType.Debit or TransactionType.Transfer)
            .OrderByDescending(t => Math.Abs(t.Amount))
            .Take(5)
            .ToList();

        if (top.Count == 0)
        {
            return Grounded("You have no recorded spending yet.", AssistantIntent.LargestTransactions, 0.85d);
        }

        var dataPoints = top
            .Select(t => $"{Math.Abs(t.Amount):C} — {(string.IsNullOrWhiteSpace(t.Description) ? t.TransactionType.ToString() : t.Description)} ({AsUtc(t.DateTime):yyyy-MM-dd})")
            .ToList();

        return Grounded(
            $"Your largest {top.Count} transactions total {top.Sum(t => Math.Abs(t.Amount)):C}.",
            AssistantIntent.LargestTransactions,
            0.85d,
            dataPoints);
    }

    private async Task<AssistantResponse> FallbackAsync(string userId, string originalQuestion, CancellationToken cancellationToken)
    {
        if (_languageModel.IsEnabled)
        {
            // Aggregate-only, PII-masked context: the model never receives raw account identifiers.
            List<Transaction> txns = await LoadUserTransactionsAsync(userId);
            SpendingInsights insights = _forecasting.AnalyzeSpending(userId: null, txns, DateTime.UtcNow);
            string context =
                $"total_debits={insights.TotalDebits:F2}; total_credits={insights.TotalCredits:F2}; " +
                $"net_cash_flow={insights.NetCashFlow:F2}; avg_debit={insights.AverageDebit:F2}; txn_count={txns.Count}";

            string answer = await _languageModel.CompleteAsync(
                systemPrompt: "You are a careful banking assistant. Answer ONLY from the supplied aggregates. " +
                              "If the data is insufficient, say so. Never invent figures.",
                userPrompt: $"Question: {originalQuestion}\nData: {context}",
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(answer))
            {
                return new AssistantResponse
                {
                    Answer = answer.Trim(),
                    Intent = AssistantIntent.Unknown,
                    Confidence = 0.5d,
                    Provider = _options.Assistant.Provider.ToString().ToLowerInvariant(),
                    Grounded = false
                };
            }
        }

        return Grounded(
            "I can help with your account balance, savings goals, and spending (e.g. \"how much did I spend on food last month?\").",
            AssistantIntent.Unknown,
            0.3d);
    }

    private async Task<List<Account>> LoadUserAccountsAsync(string userId)
    {
        User? user = await _users.GetAsync(userId);
        var accounts = new List<Account>();

        if (user?.AccountIds is { Count: > 0 })
        {
            foreach (string accountId in user.AccountIds)
            {
                if (string.IsNullOrWhiteSpace(accountId))
                {
                    continue;
                }

                Account? account = await _accounts.GetAsync(accountId);
                if (account is not null)
                {
                    accounts.Add(account);
                }
            }
        }

        return accounts;
    }

    private async Task<List<Transaction>> LoadUserTransactionsAsync(string userId)
    {
        List<Transaction> txns = await _transactions.GetForUserAsync(userId) ?? new List<Transaction>();
        int cap = _options.Assistant.MaxHistoryTransactions;

        if (txns.Count > cap)
        {
            txns = txns.OrderByDescending(t => t.DateTime).Take(cap).ToList();
        }

        return txns;
    }

    private static bool TryDetectCategory(string q, out string category, out string[] keywords)
    {
        foreach (KeyValuePair<string, string[]> entry in Categories)
        {
            if (entry.Value.Any(k => q.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                category = entry.Key;
                keywords = entry.Value;
                return true;
            }
        }

        category = string.Empty;
        keywords = Array.Empty<string>();
        return false;
    }

    private static bool MatchesCategory(string? description, string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        string d = description.ToLowerInvariant();
        return keywords.Any(k => d.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static (DateTime Start, string Label) DetectPeriod(string q, DateTime now)
    {
        if (q.Contains("today"))
        {
            return (now.Date, "today");
        }

        if (q.Contains("week"))
        {
            return (now.AddDays(-7), "the last 7 days");
        }

        if (q.Contains("year"))
        {
            return (now.AddDays(-365), "the last 12 months");
        }

        if (q.Contains("month"))
        {
            return (now.AddDays(-30), "the last 30 days");
        }

        return (DateTime.MinValue, "your history");
    }

    private static AssistantResponse Grounded(
        string answer,
        AssistantIntent intent,
        double confidence,
        IReadOnlyList<string>? dataPoints = null) => new()
    {
        Answer = answer,
        Intent = intent,
        Confidence = confidence,
        DataPoints = dataPoints ?? Array.Empty<string>(),
        Provider = "local",
        Grounded = true
    };

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
