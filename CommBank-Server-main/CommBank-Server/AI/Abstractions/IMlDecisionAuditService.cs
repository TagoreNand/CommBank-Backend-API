using CommBank.AI.Models;

namespace CommBank.AI.Abstractions;

/// <summary>
/// Append-only audit sink for automated model decisions. Writing is best-effort and must never
/// break the caller's primary flow; the idempotency lookup supports exactly-once scoring on retries.
/// </summary>
public interface IMlDecisionAuditService
{
    Task RecordAsync(MlDecisionRecord record, CancellationToken cancellationToken = default);

    Task<MlDecisionRecord?> FindByIdempotencyKeyAsync(
        string idempotencyKey,
        string decisionType,
        CancellationToken cancellationToken = default);
}
