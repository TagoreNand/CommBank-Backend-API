using CommBank.AI.Models;
using CommBank.Models;

namespace CommBank.AI.Abstractions;

/// <summary>
/// Application-service that wraps transaction creation with fraud/AML scoring, an idempotency guard,
/// a fail-open/closed resilience policy, and audit persistence. This is the seam a team would swap the
/// plain TransactionController POST onto to make the money-path risk-aware without touching the model.
/// </summary>
public interface IRiskAwareTransactionOrchestrator
{
    Task<TransactionDecision> CreateAsync(
        Transaction transaction,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);
}
