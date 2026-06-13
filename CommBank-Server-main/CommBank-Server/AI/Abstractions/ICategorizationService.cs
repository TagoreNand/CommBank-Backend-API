using CommBank.AI.Models;
using CommBank.Models;

namespace CommBank.AI.Abstractions;

/// <summary>
/// Port for suggesting tags (categories) for a transaction. The candidate taxonomy is supplied by
/// the caller so the model stays tenant-agnostic and unit-testable.
/// </summary>
public interface ICategorizationService
{
    Task<CategorizationResult> CategorizeAsync(
        Transaction transaction,
        IReadOnlyList<Tag> candidateTags,
        CancellationToken cancellationToken = default);
}
