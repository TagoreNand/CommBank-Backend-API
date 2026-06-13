using CommBank.Ledger.Models;
using CommBank.Transfers.Models;
using MongoDB.Driver;

namespace CommBank.Ledger;

/// <summary>Posts double-entry journal entries for financial events and reads them back.</summary>
public interface ILedgerService
{
    /// <summary>Writes the balanced debit/credit legs for a transfer within the caller's transaction.</summary>
    Task PostTransferAsync(IClientSessionHandle session, FundTransfer transfer, CancellationToken cancellationToken = default);

    /// <summary>Returns every ledger entry produced by a transfer (its journal).</summary>
    Task<IReadOnlyList<LedgerEntry>> GetJournalAsync(string transferId, CancellationToken cancellationToken = default);
}
