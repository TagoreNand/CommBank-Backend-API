namespace CommBank.AI.Abstractions;

/// <summary>
/// Thin seam over a hosted large-language-model provider. Kept deliberately minimal so the default
/// in-process assistant has zero external dependency; a real Anthropic/OpenAI client implements this
/// and is injected only when configured. Implementations own their own timeout, retry and key handling.
/// </summary>
public interface ILanguageModelClient
{
    /// <summary>False for the no-op default; callers must degrade gracefully when disabled.</summary>
    bool IsEnabled { get; }

    Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
