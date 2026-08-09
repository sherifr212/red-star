using System.ClientModel.Primitives;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace RedStar.Base.Agents.Unsloth;

/// <summary>
/// <see cref="IAgentResponseExtractor"/> implementation for Unsloth-backed agents (see
/// <see cref="UnslothAgentFactory.Create"/>). Unsloth's custom SSE events
/// (<c>tool_status</c>/<c>tool_start</c>/<c>tool_end</c>/<c>reasoning_summary</c>) don't match the OpenAI
/// chat-completions chunk schema, so neither the OpenAI SDK nor <c>Microsoft.Extensions.AI</c>/
/// <c>Microsoft.Agents.AI</c> model them as typed properties -- both methods below unwrap the raw JSON
/// via <see cref="TryGetRawUpdateJson"/> instead.
/// </summary>
public sealed class UnslothAgentResponseExtractor : IAgentResponseExtractor
{
    /// <summary>
    /// Extracts the human-readable label Unsloth attaches to server-side tool activity (e.g.
    /// "Searching: current year", "Reading: accuweather.com") from a streamed update, if present.
    /// Unsloth ships these as a custom top-level <c>{"type":"tool_status","content":"..."}</c> SSE event
    /// that sits outside the OpenAI chat-completions chunk schema -- see <see cref="TryGetRawUpdateJson"/>
    /// for how it's recovered. Returns null for ordinary content/reasoning chunks, other event types
    /// (<c>tool_start</c>/<c>tool_end</c>/<c>reasoning_summary</c>), or non-Unsloth/non-OpenAI backends.
    /// </summary>
    public string? TryGetToolStatus(AgentResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        using var doc = TryGetRawUpdateJson(update);
        if (doc is null ||
            !doc.RootElement.TryGetProperty("type", out var typeProperty) ||
            typeProperty.GetString() != "tool_status" ||
            !doc.RootElement.TryGetProperty("content", out var contentProperty))
        {
            return null;
        }

        var content = contentProperty.GetString();
        return string.IsNullOrEmpty(content) ? null : content;
    }

    /// <summary>
    /// Extracts every hit (title + URL) from a completed Unsloth <c>web_search</c> tool call, if
    /// <paramref name="update"/> is the <c>tool_end</c> event for one. Only fires for general queries, not
    /// single-page fetches (used to read one already-found site, covered by a status label from
    /// <see cref="TryGetToolStatus"/> instead): the <c>tool_end</c> event carries no <c>arguments</c> to
    /// distinguish the two by (only its paired <c>tool_start</c> does, and updates are handled one at a
    /// time with no correlation between them), so this instead relies on Unsloth's two <c>result</c>
    /// shapes being structurally distinct -- a query's hits come back as one plain-text blob
    /// (<c>"Title: ...\nURL: ...\nSnippet: ...\n\n---\n\n..."</c>, ending in an instructional paragraph
    /// with neither a Title nor a URL line, which is what the per-block filter below excludes), while a
    /// page fetch's <c>result</c> is either raw page content or an error string, neither of which has any
    /// <c>Title:</c>/<c>URL:</c> lines to match. Either way, nothing here is streamed incrementally by the
    /// server -- callers wanting a "revealing" effect need to fake it client-side. Callers that run
    /// multiple searches in one turn (see <c>ChatCommandHandler.StageBox.Apply</c>) are expected to
    /// accumulate results across calls rather than rely on this method capping or deduplicating across
    /// calls -- it only ever returns the hits from this one <c>tool_end</c> event.
    /// </summary>
    public IReadOnlyList<WebSearchResult>? TryGetWebSearchResults(AgentResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        using var doc = TryGetRawUpdateJson(update);
        if (doc is null ||
            !doc.RootElement.TryGetProperty("type", out var typeProperty) || typeProperty.GetString() != "tool_end" ||
            !doc.RootElement.TryGetProperty("tool_name", out var nameProperty) || nameProperty.GetString() != "web_search" ||
            !doc.RootElement.TryGetProperty("result", out var resultProperty))
        {
            return null;
        }

        var resultText = resultProperty.GetString();
        if (string.IsNullOrEmpty(resultText))
        {
            return null;
        }

        var results = new List<WebSearchResult>();
        foreach (var block in resultText.Split("\n\n---\n\n"))
        {
            string? title = null;
            string? url = null;
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("Title: ", StringComparison.Ordinal))
                {
                    title = line["Title: ".Length..].Trim();
                }
                else if (line.StartsWith("URL: ", StringComparison.Ordinal))
                {
                    url = line["URL: ".Length..].Trim();
                }
            }

            if (title is null || url is null)
            {
                continue;
            }

            results.Add(new WebSearchResult(title, url));
        }

        return results.Count == 0 ? null : results;
    }

    /// <summary>
    /// Unwraps a streamed update down to the OpenAI SDK's raw JSON, if it came from an OpenAI-backed agent.
    /// Unsloth's custom SSE events (<c>tool_status</c>/<c>tool_start</c>/<c>tool_end</c>/<c>reasoning_summary</c>)
    /// don't match the OpenAI chat-completions chunk schema, so neither the OpenAI SDK nor
    /// <c>Microsoft.Extensions.AI</c>/<c>Microsoft.Agents.AI</c> model them as typed properties -- they only
    /// survive by round-tripping through <see cref="AgentResponseUpdate.RawRepresentation"/>, unwrapped
    /// through the nested <see cref="ChatResponseUpdate.RawRepresentation"/> layer down to the OpenAI SDK's
    /// <see cref="StreamingChatCompletionUpdate"/>, then re-serialized via its <see cref="IJsonModel{T}"/>
    /// implementation (which preserves unmodeled properties). Returns null for non-OpenAI backends.
    /// </summary>
    private static JsonDocument? TryGetRawUpdateJson(AgentResponseUpdate update)
    {
        object? raw = update.RawRepresentation;
        while (raw is ChatResponseUpdate wrapped)
        {
            raw = wrapped.RawRepresentation;
        }

        return raw is IJsonModel<StreamingChatCompletionUpdate> jsonModel
            ? JsonDocument.Parse(jsonModel.Write(ModelReaderWriterOptions.Json))
            : null;
    }
}
