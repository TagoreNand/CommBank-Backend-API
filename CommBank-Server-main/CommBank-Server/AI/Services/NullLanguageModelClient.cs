using CommBank.AI.Abstractions;

namespace CommBank.AI.Services;

/// <summary>
/// Default, no-op language-model client. Keeps the assistant fully in-process and dependency-free
/// unless a real hosted provider (Anthropic/OpenAI) is registered in its place. Callers must check
/// <see cref="IsEnabled"/> and degrade gracefully.
/// </summary>
public sealed class NullLanguageModelClient : ILanguageModelClient
{
    public bool IsEnabled => false;

    public Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Empty);
}
