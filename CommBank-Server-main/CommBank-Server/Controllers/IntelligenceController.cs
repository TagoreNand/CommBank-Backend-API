using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.Models;
using CommBank.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommBank.Controllers;

/// <summary>
/// Read/analyze surface for the intelligence module plus a risk-aware transaction create. Thin HTTP
/// façade: it loads domain data and delegates all decisions to the AI ports. Requires an authenticated
/// caller; assistant and insights are scoped to the supplied user id.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class IntelligenceController : ControllerBase
{
    private readonly IRiskScoringService _risk;
    private readonly IRiskAwareTransactionOrchestrator _orchestrator;
    private readonly ICategorizationService _categorizer;
    private readonly IGoalForecastingService _forecasting;
    private readonly IFinancialAssistantService _assistant;
    private readonly ITransactionsService _transactions;
    private readonly IGoalsService _goals;
    private readonly ITagsService _tags;
    private readonly IUsersService _users;
    private readonly IAccountsService _accounts;

    public IntelligenceController(
        IRiskScoringService risk,
        IRiskAwareTransactionOrchestrator orchestrator,
        ICategorizationService categorizer,
        IGoalForecastingService forecasting,
        IFinancialAssistantService assistant,
        ITransactionsService transactions,
        IGoalsService goals,
        ITagsService tags,
        IUsersService users,
        IAccountsService accounts)
    {
        _risk = risk;
        _orchestrator = orchestrator;
        _categorizer = categorizer;
        _forecasting = forecasting;
        _assistant = assistant;
        _transactions = transactions;
        _goals = goals;
        _tags = tags;
        _users = users;
        _accounts = accounts;
    }

    /// <summary>Score a transaction for fraud/AML risk WITHOUT persisting it.</summary>
    [HttpPost("risk/score")]
    public async Task<ActionResult<RiskAssessment>> ScoreRisk([FromBody] Transaction transaction)
    {
        if (transaction is null)
        {
            return BadRequest("A transaction body is required.");
        }

        RiskScoringContext context = await BuildContextAsync(transaction);
        RiskAssessment assessment = await _risk.ScoreAsync(transaction, context, HttpContext.RequestAborted);
        return Ok(assessment);
    }

    /// <summary>Risk-aware create: scores, then persists or blocks per decision. Honours the Idempotency-Key header.</summary>
    [HttpPost("transactions")]
    public async Task<IActionResult> CreateScored([FromBody] Transaction transaction)
    {
        if (transaction is null)
        {
            return BadRequest("A transaction body is required.");
        }

        string? idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out Microsoft.Extensions.Primitives.StringValues v)
            ? v.ToString()
            : null;

        TransactionDecision decision = await _orchestrator.CreateAsync(transaction, idempotencyKey, HttpContext.RequestAborted);

        return decision.Status switch
        {
            "blocked" or "blocked_scoring_error" => StatusCode(StatusCodes.Status422UnprocessableEntity, decision),
            "created_flagged_for_review" => StatusCode(StatusCodes.Status202Accepted, decision),
            "idempotent_replay" => Ok(decision),
            _ => StatusCode(StatusCodes.Status201Created, decision)
        };
    }

    /// <summary>Suggest tags (categories) for a transaction against the tenant taxonomy.</summary>
    [HttpPost("categorize")]
    public async Task<ActionResult<CategorizationResult>> Categorize([FromBody] Transaction transaction)
    {
        if (transaction is null)
        {
            return BadRequest("A transaction body is required.");
        }

        List<Tag> tags = await _tags.GetAsync();
        CategorizationResult result = await _categorizer.CategorizeAsync(transaction, tags, HttpContext.RequestAborted);
        return Ok(result);
    }

    /// <summary>Forecast completion for a single savings goal.</summary>
    [HttpGet("goals/{id:length(24)}/forecast")]
    public async Task<ActionResult<GoalForecast>> ForecastGoal(string id)
    {
        Goal? goal = await _goals.GetAsync(id);
        if (goal is null)
        {
            return NotFound();
        }

        List<Transaction> userTxns = goal.UserId is not null
            ? await _transactions.GetForUserAsync(goal.UserId) ?? new List<Transaction>()
            : new List<Transaction>();

        List<Transaction> goalTxns = userTxns.Where(t => t.GoalId is not null && t.GoalId == id).ToList();
        GoalForecast forecast = _forecasting.Forecast(goal, goalTxns, DateTime.UtcNow);
        return Ok(forecast);
    }

    /// <summary>Aggregate spending insights and anomalies for a user.</summary>
    [HttpGet("users/{id:length(24)}/insights")]
    public async Task<ActionResult<SpendingInsights>> Insights(string id)
    {
        List<Transaction> txns = await _transactions.GetForUserAsync(id) ?? new List<Transaction>();
        SpendingInsights insights = _forecasting.AnalyzeSpending(id, txns, DateTime.UtcNow);
        return Ok(insights);
    }

    /// <summary>Ask the in-process financial assistant a natural-language question, scoped to one user.</summary>
    [HttpPost("assistant")]
    public async Task<ActionResult<AssistantResponse>> Assistant([FromBody] AssistantQuery query)
    {
        if (query is null || string.IsNullOrWhiteSpace(query.UserId))
        {
            return BadRequest("A query with a userId is required.");
        }

        AssistantResponse response = await _assistant.AskAsync(query, HttpContext.RequestAborted);
        return Ok(response);
    }

    private async Task<RiskScoringContext> BuildContextAsync(Transaction transaction)
    {
        var recent = new List<Transaction>();
        if (!string.IsNullOrWhiteSpace(transaction.UserId))
        {
            recent = await _transactions.GetForUserAsync(transaction.UserId) ?? new List<Transaction>();
        }

        Account? account = null;
        if (!string.IsNullOrWhiteSpace(transaction.UserId))
        {
            User? user = await _users.GetAsync(transaction.UserId);
            if (user?.AccountIds is { Count: 1 } && !string.IsNullOrWhiteSpace(user.AccountIds[0]))
            {
                account = await _accounts.GetAsync(user.AccountIds[0]);
            }
        }

        return new RiskScoringContext
        {
            Account = account,
            RecentUserTransactions = recent,
            NowUtc = DateTime.UtcNow
        };
    }
}
