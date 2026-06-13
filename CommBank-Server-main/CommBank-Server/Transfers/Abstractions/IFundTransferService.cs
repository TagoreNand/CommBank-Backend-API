using CommBank.Transfers.Models;

namespace CommBank.Transfers.Abstractions;

/// <summary>
/// Moves funds between two accounts atomically. Implementations must guarantee all-or-nothing semantics
/// (no partial debit/credit), at-most-once execution per idempotency key, and lost-update protection.
/// </summary>
public interface IFundTransferService
{
    Task<TransferResult> TransferAsync(
        TransferRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);
}
