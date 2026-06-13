using CommBank.Ledger.Models;
using CommBank.Transfers.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CommBank.Ledger;

/// <summary>
/// Double-entry ledger. Each transfer produces a journal of two balanced legs — a debit on the source and
/// a credit on the destination. The balance invariant (sum of debits == sum of credits) is enforced before
/// anything is written, and the legs are inserted inside the caller's transaction so they commit atomically
/// with the balance mutations.
/// </summary>
public sealed class LedgerService : ILedgerService
{
    private readonly IMongoCollection<LedgerEntry> _entries;

    public LedgerService(IMongoDatabase database) =>
        _entries = database.GetCollection<LedgerEntry>("LedgerEntries");

    public async Task PostTransferAsync(IClientSessionHandle session, FundTransfer transfer, CancellationToken cancellationToken = default)
    {
        string journalId = ObjectId.GenerateNewId().ToString();

        var legs = new[]
        {
            new LedgerEntry
            {
                JournalId = journalId,
                AccountId = transfer.SourceAccountId,
                Direction = LedgerDirection.Debit,
                Amount = transfer.Amount,
                TransferId = transfer.Id,
                Description = transfer.Description
            },
            new LedgerEntry
            {
                JournalId = journalId,
                AccountId = transfer.DestinationAccountId,
                Direction = LedgerDirection.Credit,
                Amount = transfer.Amount,
                TransferId = transfer.Id,
                Description = transfer.Description
            }
        };

        decimal debits = legs.Where(e => e.Direction == LedgerDirection.Debit).Sum(e => e.Amount);
        decimal credits = legs.Where(e => e.Direction == LedgerDirection.Credit).Sum(e => e.Amount);
        if (debits != credits)
        {
            throw new LedgerImbalanceException(journalId, debits, credits);
        }

        await _entries.InsertManyAsync(session, legs, options: null, cancellationToken);
    }

    public async Task<IReadOnlyList<LedgerEntry>> GetJournalAsync(string transferId, CancellationToken cancellationToken = default) =>
        await _entries.Find(e => e.TransferId == transferId).ToListAsync(cancellationToken);
}

/// <summary>Thrown when a journal's debits and credits do not net to zero — a hard accounting invariant.</summary>
public sealed class LedgerImbalanceException : Exception
{
    public LedgerImbalanceException(string journalId, decimal debits, decimal credits)
        : base($"Ledger journal '{journalId}' does not balance: debits {debits:0.00} != credits {credits:0.00}.")
    {
    }
}
