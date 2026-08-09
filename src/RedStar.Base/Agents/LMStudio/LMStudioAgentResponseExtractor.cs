using Microsoft.Agents.AI;

namespace RedStar.Base.Agents.LMStudio;

/// <summary>
/// <see cref="IAgentResponseExtractor"/> implementation for LM Studio-backed agents (see
/// <see cref="LMStudioAgentFactory.Create"/>). Unlike Unsloth, LM Studio's OpenAI-compatible streaming
/// responses carry no custom side-channel SSE events (no <c>tool_status</c>/<c>tool_end</c>) -- reasoning
/// content and tool calls are already the standard OpenAI shapes <c>Microsoft.Extensions.AI</c> models
/// directly (see the <c>TextReasoningContent</c> handling in
/// <c>RedStar.Cli.ChatCommandHandler.ProduceStageEventsAsync</c>), so there is nothing to unwrap here --
/// both methods always return null.
///
/// Kept as an explicit no-op implementation rather than reusing
/// <see cref="RedStar.Base.Agents.Unsloth.UnslothAgentResponseExtractor"/> for the LM Studio path: that
/// class would in practice also always return null against LM Studio's raw JSON (its checks look for a
/// <c>type</c> property value LM Studio never sends), but only by accident of what it happens to check for,
/// not because it was designed to recognize "this isn't Unsloth." Selecting an agent-specific extractor
/// per agent (see <c>RedStar.Cli.ChatCommandHandler.RunAsync</c>'s default) keeps that correct on purpose.
/// </summary>
public sealed class LMStudioAgentResponseExtractor : IAgentResponseExtractor
{
    public string? TryGetToolStatus(AgentResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return null;
    }

    public IReadOnlyList<WebSearchResult>? TryGetWebSearchResults(AgentResponseUpdate update, int maxResults = 5)
    {
        ArgumentNullException.ThrowIfNull(update);
        return null;
    }
}
