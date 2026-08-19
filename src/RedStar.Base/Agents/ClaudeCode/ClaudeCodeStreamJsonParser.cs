using System.Text.Json;

namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// One line of <c>claude --print --output-format stream-json --include-partial-messages</c> output, reduced
/// to what RedStar's rendering/session bookkeeping needs. At most one of <see cref="TextDelta"/>/
/// <see cref="ToolUseName"/>/<see cref="Result"/> is set. <see cref="RawJson"/> is always the original line,
/// carried through as <c>ChatResponseUpdate.RawRepresentation</c> so
/// <see cref="ClaudeCodeAgentResponseExtractor"/> can recover it from a streamed
/// <c>Microsoft.Agents.AI.AgentResponseUpdate</c> the same way Unsloth's extractor recovers its own raw JSON
/// -- see the remarks there.
/// </summary>
public readonly record struct ClaudeCodeLine(string RawJson, string? TextDelta, string? ToolUseName, ClaudeCodeResultMessage? Result);

/// <summary>The terminal <c>{"type":"result",...}</c> line of one turn. Field names/shapes below were
/// captured from a real <c>claude -p ... --output-format json</c> run (v2.1.224), not just documentation --
/// see the shape notes on each property for what's actually deserialized vs. ignored.</summary>
public sealed record ClaudeCodeResultMessage(
    string? SessionId, bool IsError, string? Subtype, double? TotalCostUsd, int? OutputTokens, string? ResultText);

/// <summary>
/// Parses <c>claude</c>'s stream-json protocol. A pure function over raw JSON lines -- no process I/O, no
/// exceptions for malformed/uninteresting input (returns null instead) -- so it's unit-testable against
/// captured fixture lines the same way <c>UnslothAgentResponseExtractor</c> is tested against fixture JSON,
/// independent of <c>IClaudeCodeProcessRunner</c>'s actual process spawning.
/// </summary>
public static class ClaudeCodeStreamJsonParser
{
    /// <summary>
    /// Parses one line. Returns null for every line type RedStar's chat rendering/session bookkeeping has no
    /// use for -- <c>system</c> (session init/status), <c>rate_limit_event</c>, <c>assistant</c> (the
    /// complete message, redundant with the <c>content_block_delta</c> chunks already accumulated),
    /// <c>stream_event</c> subtypes other than <c>content_block_delta</c>/text_delta and
    /// <c>content_block_start</c>/tool_use (<c>message_start</c>, <c>content_block_stop</c>,
    /// <c>message_delta</c>, <c>message_stop</c>) -- as well as blank or malformed JSON.
    /// </summary>
    public static ClaudeCodeLine? TryParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeProperty))
            {
                return null;
            }

            return typeProperty.GetString() switch
            {
                "stream_event" => ParseStreamEvent(line, root),
                "result" => new ClaudeCodeLine(line, null, null, ParseResult(root)),
                _ => null,
            };
        }
    }

    /// <summary>
    /// Unwraps the raw Claude API event nested under <c>{"type":"stream_event","event":{...}}</c>. Only two
    /// of the event's own <c>type</c> values carry anything RedStar renders: <c>content_block_delta</c> with
    /// a <c>text_delta</c> (the token-by-token answer text -- see
    /// <c>RedStar.Cli.ChatCommandHandler.TurnStage.Generating</c>) and <c>content_block_start</c> for a
    /// <c>tool_use</c> block (the tool's name, surfaced as a tool-status label by
    /// <see cref="ClaudeCodeAgentResponseExtractor.TryGetToolStatus"/>).
    /// </summary>
    private static ClaudeCodeLine? ParseStreamEvent(string line, JsonElement root)
    {
        if (!root.TryGetProperty("event", out var evt) ||
            evt.ValueKind != JsonValueKind.Object ||
            !evt.TryGetProperty("type", out var eventTypeProperty))
        {
            return null;
        }

        switch (eventTypeProperty.GetString())
        {
            case "content_block_delta":
                if (evt.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("type", out var deltaTypeProperty) &&
                    deltaTypeProperty.GetString() == "text_delta" &&
                    delta.TryGetProperty("text", out var textProperty) &&
                    textProperty.GetString() is { Length: > 0 } text)
                {
                    return new ClaudeCodeLine(line, text, null, null);
                }

                return null;

            case "content_block_start":
                if (evt.TryGetProperty("content_block", out var block) &&
                    block.TryGetProperty("type", out var blockTypeProperty) &&
                    blockTypeProperty.GetString() == "tool_use" &&
                    block.TryGetProperty("name", out var nameProperty) &&
                    nameProperty.GetString() is { Length: > 0 } name)
                {
                    return new ClaudeCodeLine(line, null, name, null);
                }

                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Reads the fields RedStar needs from a <c>result</c> line's much larger real shape (which also
    /// includes, among others, <c>duration_ms</c>/<c>num_turns</c>/<c>modelUsage</c>/<c>permission_denials</c>
    /// -- none of which anything here reads yet). <c>usage.output_tokens</c> feeds the same
    /// <c>UsageContent</c>/token-speed-footer path Unsloth's <c>stream_options.include_usage</c> and LM
    /// Studio's request field do -- see <c>ClaudeCodeChatClient</c>.
    /// </summary>
    private static ClaudeCodeResultMessage ParseResult(JsonElement root)
    {
        var isError = root.TryGetProperty("is_error", out var isErrorProperty) && isErrorProperty.ValueKind == JsonValueKind.True;
        var sessionId = root.TryGetProperty("session_id", out var sessionIdProperty) ? sessionIdProperty.GetString() : null;
        var subtype = root.TryGetProperty("subtype", out var subtypeProperty) ? subtypeProperty.GetString() : null;
        var resultText = root.TryGetProperty("result", out var resultProperty) && resultProperty.ValueKind == JsonValueKind.String
            ? resultProperty.GetString()
            : null;

        double? totalCostUsd = null;
        if (root.TryGetProperty("total_cost_usd", out var costProperty) && costProperty.ValueKind == JsonValueKind.Number)
        {
            totalCostUsd = costProperty.GetDouble();
        }

        int? outputTokens = null;
        if (root.TryGetProperty("usage", out var usage) &&
            usage.ValueKind == JsonValueKind.Object &&
            usage.TryGetProperty("output_tokens", out var outputTokensProperty) &&
            outputTokensProperty.ValueKind == JsonValueKind.Number)
        {
            outputTokens = outputTokensProperty.GetInt32();
        }

        return new ClaudeCodeResultMessage(sessionId, isError, subtype, totalCostUsd, outputTokens, resultText);
    }
}