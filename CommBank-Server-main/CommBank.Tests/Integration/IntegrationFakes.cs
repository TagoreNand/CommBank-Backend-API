using CommBank.AI.Abstractions;
using CommBank.AI.Models;
using CommBank.Models;

namespace CommBank.Tests.Integration;

/// <summary>Always approves, so transfer integration tests isolate transactional behaviour from risk policy.</summary>
public sealed class FakeApproveRiskScoringService : IRiskScoringService
{
    public Task<RiskAssessment> ScoreAsync(
        Transaction transaction,
        RiskScoringContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(RiskAssessment.Approved(transaction.Id, transaction.UserId, "integration-test"));
}

/// <summary>No-op audit sink for tests.</summary>
public sealed class FakeMlDecisionAuditService : IMlDecisionAuditService
{
    public Task RecordAsync(MlDecisionRecord record, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<MlDecisionRecord?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        string decisionType,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<MlDecisionRecord?>(null);
}
