using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.Models;

namespace CommBank.Tests.Fake;

/// <summary>Test double that approves and "persists" any transaction without scoring.</summary>
public class FakeRiskAwareTransactionOrchestrator : IRiskAwareTransactionOrchestrator
{
    public Task<TransactionDecision> CreateAsync(
        Transaction transaction,
        string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new TransactionDecision
        {
            Assessment = new RiskAssessment
            {
                TransactionId = transaction.Id,
                UserId = transaction.UserId,
                Decision = RiskDecision.Approve,
                Band = RiskBand.Low
            },
            Persisted = true,
            Status = "created"
        });
}
