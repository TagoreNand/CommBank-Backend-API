using CommBank.AI.Models;
using CommBank.Models;

namespace CommBank.AI.Abstractions;

/// <summary>
/// Port for transaction risk/fraud/AML scoring. Implementations must be deterministic for a given
/// (transaction, context) pair and must never throw for ordinary inputs — resilience policy is the
/// orchestrator's job. Swap the heuristic adapter for an ML.NET or remote model without changing callers.
/// </summary>
public interface IRiskScoringService
{
    Task<RiskAssessment> ScoreAsync(
        Transaction transaction,
        RiskScoringContext context,
        CancellationToken cancellationToken = default);
}
