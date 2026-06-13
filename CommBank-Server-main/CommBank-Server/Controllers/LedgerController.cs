using CommBank.Ledger;
using CommBank.Ledger.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommBank.Controllers;

/// <summary>Read access to the double-entry ledger.</summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class LedgerController : ControllerBase
{
    private readonly ILedgerService _ledger;

    public LedgerController(ILedgerService ledger) => _ledger = ledger;

    /// <summary>Returns a transfer's journal (all postings) plus a flag proving debits equal credits.</summary>
    [HttpGet("transfers/{transferId:length(24)}")]
    public async Task<IActionResult> GetJournal(string transferId)
    {
        IReadOnlyList<LedgerEntry> entries = await _ledger.GetJournalAsync(transferId, HttpContext.RequestAborted);
        if (entries.Count == 0)
        {
            return NotFound();
        }

        decimal debits = entries.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount);
        decimal credits = entries.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount);

        return Ok(new
        {
            transferId,
            entries,
            totalDebits = debits,
            totalCredits = credits,
            balanced = debits == credits
        });
    }
}
