using CommBank.AI.Models;

namespace CommBank.AI.Abstractions;

/// <summary>
/// Port for the natural-language financial assistant. Answers must be grounded in the requesting
/// user's own data; the default adapter is fully in-process, with an optional hosted-LLM backend.
/// </summary>
public interface IFinancialAssistantService
{
    Task<AssistantResponse> AskAsync(AssistantQuery query, CancellationToken cancellationToken = default);
}
