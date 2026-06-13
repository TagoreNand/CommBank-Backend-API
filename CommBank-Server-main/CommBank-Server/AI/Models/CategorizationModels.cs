namespace CommBank.AI.Models;

/// <summary>A candidate tag for a transaction with an explainable confidence.</summary>
/// <param name="TagId">Id of an existing <see cref="CommBank.Models.Tag"/>.</param>
/// <param name="TagName">Resolved tag name at evaluation time.</param>
/// <param name="Confidence">Confidence in [0,1].</param>
/// <param name="Rationale">Which signal matched, for transparency.</param>
public sealed record TagSuggestion(string TagId, string TagName, double Confidence, string Rationale);

/// <summary>Result of categorizing a single transaction against the tenant's tag taxonomy.</summary>
public sealed class CategorizationResult
{
    public string? TransactionId { get; init; }

    /// <summary>Suggestions ordered by descending confidence.</summary>
    public IReadOnlyList<TagSuggestion> Suggestions { get; init; } = Array.Empty<TagSuggestion>();

    public TagSuggestion? Best => Suggestions.Count > 0 ? Suggestions[0] : null;

    /// <summary>True when <see cref="Best"/> cleared the auto-apply confidence threshold.</summary>
    public bool AutoApplied { get; init; }

    public string ModelVersion { get; init; } = "0.0.0";

    public DateTime EvaluatedAtUtc { get; init; } = DateTime.UtcNow;
}
