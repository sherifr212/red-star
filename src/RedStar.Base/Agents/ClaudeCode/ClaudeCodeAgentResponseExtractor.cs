using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// <see cref="IAgentResponseExtractor"/> implementation for ClaudeCode-backed agents (see
/// <see cref="ClaudeCodeAgentFactory.Create"/>). Unlike <c>UnslothAgentResponseExtractor</c>, which has to
/// unwrap the OpenAI SDK's raw JSON model to recover Unsloth's custom SSE events,
/// <see cref="ClaudeCodeChatClient"/> controls <c>ChatResponseUpdate.RawRepresentation</c> directly -- it's
/// always the original stream-json line as a plain string (see <see cref="ClaudeCodeStreamJsonParser"/>), so
/// recovering it here is a single pattern match, not a multi-layer unwrap.
/// </summary>
public sealed class ClaudeCodeAgentResponseExtractor : IAgentResponseExtractor
{
    /// <summary>
    /// "Running: &lt;tool name&gt;" for a <c>content_block_start</c>/<c>tool_use</c> event, if
    /// <paramref name="update"/> carries one -- reused by <c>ChatCommandHandler</c> as the same
    /// <c>TurnStage.Searching</c> stage Unsloth's <c>tool_status</c> label drives, even though the label text
    /// itself is generic tool activity rather than Unsloth's more specific "Searching:"/"Reading:" phrasing
    /// (Claude Code's stream-json protocol has no equivalent human-readable status string -- only the raw
    /// tool name and its JSON arguments, which stream in as a separate <c>input_json_delta</c> this extractor
    /// doesn't surface). Returns null for ordinary text/reasoning chunks, non-tool_use event types, or
    /// non-ClaudeCode backends.
    /// </summary>
    public string? TryGetToolStatus(AgentResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (TryGetRawLine(update) is not { } line)
        {
            return null;
        }

        return ClaudeCodeStreamJsonParser.TryParseLine(line) is { ToolUseName: { Length: > 0 } name } ? $"Running: {name}" : null;
    }

    /// <summary>
    /// Always null. Claude Code's <c>WebSearch</c> tool surfaces through the same generic
    /// <c>tool_use</c>/<c>tool_result</c> content-block shape as every other tool -- there is no
    /// Unsloth-style dedicated <c>tool_end</c> event carrying a structured hit list to parse, so there's
    /// nothing for this method to extract beyond the generic "Running: WebSearch" label
    /// <see cref="TryGetToolStatus"/> already produces.
    /// </summary>
    public IReadOnlyList<WebSearchResult>? TryGetWebSearchResults(AgentResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return null;
    }

    private static string? TryGetRawLine(AgentResponseUpdate update) =>
        update.RawRepresentation is ChatResponseUpdate { RawRepresentation: string line } ? line : null;
}
