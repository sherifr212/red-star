using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace RedStar.Base.Agents.GoogleAI;

/// <summary>
/// <see cref="IAgentResponseExtractor"/> implementation for Google AI-backed agents.
/// Google AI Studio's standard chat API follows the OpenAI-compatible schema for basic
/// completions but does not include tool-status or web-search result extraction support
/// at this time. Both methods return null as there are no Google AI-specific SSE events
/// to unwrap beyond the standard OpenAI schema.
/// </summary>
public sealed class GoogleAIAgentResponseExtractor : IAgentResponseExtractor
{
    public string? TryGetToolStatus(AgentResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return null;
    }

    public IReadOnlyList<WebSearchResult>? TryGetWebSearchResults(AgentResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return null;
    }
}
