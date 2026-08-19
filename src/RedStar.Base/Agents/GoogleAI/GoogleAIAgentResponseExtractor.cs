using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace RedStar.Base.Agents.GoogleAI;

/// <summary>
/// <see cref="IAgentResponseExtractor"/> implementation for Google AI-backed agents. Gemini's
/// "thinking mode" reasoning trace is not routed through this extractor -- the <c>Google.GenAI</c>
/// SDK's <c>IChatClient</c> already surfaces it as a distinct <c>TextReasoningContent</c>, which
/// <c>RedStar.Cli.ChatEngine</c> picks up generically for every agent. This extractor exists only for
/// tool-status labels and completed web-search hits, neither of which Gemini exposes as a side-channel
/// SSE event the way Unsloth does, so both methods return null.
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