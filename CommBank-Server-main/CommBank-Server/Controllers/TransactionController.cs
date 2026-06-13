using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.Models;
using CommBank.Services;

namespace CommBank.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TransactionController : ControllerBase
{
    private readonly ITransactionsService _transactionsService;
    private readonly IRiskAwareTransactionOrchestrator _orchestrator;

    public TransactionController(
        ITransactionsService transactionsService,
        IRiskAwareTransactionOrchestrator orchestrator)
    {
        _transactionsService = transactionsService;
        _orchestrator = orchestrator;
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<List<Transaction>> Get() =>
        await _transactionsService.GetAsync();

    /// <summary>Paged list of transactions, newest first. Admin only.</summary>
    [Authorize(Roles = "Admin")]
    [HttpGet("page")]
    public async Task<ActionResult<PagedResult<Transaction>>> GetPaged([FromQuery] PageQuery query)
    {
        (List<Transaction> items, long total) = await _transactionsService.GetPagedAsync(query.Skip, query.PageSize);
        return Ok(new PagedResult<Transaction>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }

    [HttpGet("User/{id:length(24)}")]
    public async Task<List<Transaction>?> GetForUser(string id) =>
        await _transactionsService.GetForUserAsync(id);

    [HttpGet("{id:length(24)}")]
    public async Task<ActionResult<Transaction>> Get(string id)
    {
        var transaction = await _transactionsService.GetAsync(id);

        if (transaction is null)
        {
            return NotFound();
        }

        return transaction;
    }

    // Risk-aware create: scored by the orchestrator, then persisted or blocked.
    // Honours the Idempotency-Key header so retries never double-post.
    [HttpPost]
    public async Task<IActionResult> Post(Transaction newTransaction)
    {
        string? idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var values)
            ? values.ToString()
            : null;

        TransactionDecision decision = await _orchestrator.CreateAsync(
            newTransaction, idempotencyKey, HttpContext.RequestAborted);

        return decision.Status switch
        {
            "blocked" or "blocked_scoring_error" => StatusCode(StatusCodes.Status422UnprocessableEntity, decision),
            "created_flagged_for_review" => StatusCode(StatusCodes.Status202Accepted, decision),
            "idempotent_replay" => Ok(decision),
            _ => CreatedAtAction(nameof(Get), new { id = decision.Assessment.TransactionId }, decision)
        };
    }

    [HttpPut("{id:length(24)}")]
    public async Task<IActionResult> Update(string id, Transaction updatedTransaction)
    {
        var transaction = await _transactionsService.GetAsync(id);

        if (transaction is null)
        {
            return NotFound();
        }

        updatedTransaction.Id = transaction.Id;

        await _transactionsService.UpdateAsync(id, updatedTransaction);

        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:length(24)}")]
    public async Task<IActionResult> Delete(string id)
    {
        var transaction = await _transactionsService.GetAsync(id);

        if (transaction is null)
        {
            return NotFound();
        }

        await _transactionsService.RemoveAsync(id);

        return NoContent();
    }
}
