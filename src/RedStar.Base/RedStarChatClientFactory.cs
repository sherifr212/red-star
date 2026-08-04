using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using RedStar.Base.Telemetry;

namespace RedStar.Base;

public static class RedStarChatClientFactory
{
    /// <summary>
    /// Builds an <see cref="AIAgent"/> backed by the Unsloth Studio server. <paramref name="instructions"/>
    /// becomes the agent's system prompt (merged into <see cref="ChatOptions.Instructions"/> on every run
    /// by <see cref="ChatClientAgent"/>) rather than a message the caller has to manage.
    /// </summary>
    public static AIAgent Create(RedStarOptions options, string modelId, string? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(modelId);

        RedStarTelemetry.CreateLogger("RedStar.RedStarChatClientFactory")
            .LogInformation("Building chat agent for model {ModelId}", modelId);

        var hasApiKey = !string.IsNullOrEmpty(options.ApiKey);

        var httpClient = new HttpClient(new ConditionalAuthHandler(stripAuthHeader: !hasApiKey));
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options.BaseUrl),
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        var credential = new ApiKeyCredential(hasApiKey ? options.ApiKey : "not-needed");
        var openAiClient = new OpenAIClient(credential, clientOptions);

        var chatOptions = CreateChatOptions(options) ?? new ChatOptions();
        chatOptions.Instructions = instructions;

        return openAiClient.GetChatClient(modelId).AsAIAgent(new ChatClientAgentOptions { ChatOptions = chatOptions });
    }

    /// <summary>
    /// Builds the <see cref="ChatOptions"/> to pass alongside each request, applying
    /// Unsloth-specific fields (not modeled by the OpenAI SDK) via <see cref="ChatCompletionOptions.Patch"/>.
    /// Returns null when no such fields are needed.
    /// </summary>
    public static ChatOptions? CreateChatOptions(RedStarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.WebSearchEnabled)
        {
            return null;
        }

        var completionOptions = new ChatCompletionOptions();
#pragma warning disable SCME0001 // Patch is an evaluation-only OpenAI SDK API for fields it doesn't model yet.
        completionOptions.Patch.Set("$.enable_tools"u8, true);
        completionOptions.Patch.Set("$.enabled_tools"u8, BinaryData.FromString("""["web_search"]"""));
#pragma warning restore SCME0001

        return new ChatOptions { RawRepresentationFactory = _ => completionOptions };
    }

    /// <summary>
    /// Extracts the human-readable label Unsloth attaches to server-side tool activity (e.g.
    /// "Searching: current year", "Reading: accuweather.com") from a streamed update, if present.
    /// Unsloth ships these as a custom top-level <c>{"type":"tool_status","content":"..."}</c> SSE event
    /// that sits outside the OpenAI chat-completions chunk schema -- see <see cref="TryGetRawUpdateJson"/>
    /// for how it's recovered. Returns null for ordinary content/reasoning chunks, other event types
    /// (<c>tool_start</c>/<c>tool_end</c>/<c>reasoning_summary</c>), or non-Unsloth/non-OpenAI backends.
    /// </summary>
    public static string? TryGetToolStatus(AgentResponseUpdate update)
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
    /// Extracts the top <paramref name="maxResults"/> hits (title + URL) from a completed Unsloth
    /// <c>web_search</c> tool call, if <paramref name="update"/> is the <c>tool_end</c> event for one.
    /// Only fires for general queries, not single-page fetches (used to read one already-found site,
    /// covered by a status label from <see cref="TryGetToolStatus"/> instead): the <c>tool_end</c> event
    /// carries no <c>arguments</c> to distinguish the two by (only its paired <c>tool_start</c> does, and
    /// updates are handled one at a time with no correlation between them), so this instead relies on
    /// Unsloth's two <c>result</c> shapes being structurally distinct -- a query's hits come back as one
    /// plain-text blob (<c>"Title: ...\nURL: ...\nSnippet: ...\n\n---\n\n..."</c>, ending in an
    /// instructional paragraph with neither a Title nor a URL line, which is what the per-block filter
    /// below excludes), while a page fetch's <c>result</c> is either raw page content or an error string,
    /// neither of which has any <c>Title:</c>/<c>URL:</c> lines to match. Either way, nothing here is
    /// streamed incrementally by the server -- callers wanting a "revealing" effect need to fake it
    /// client-side.
    /// </summary>
    public static IReadOnlyList<WebSearchResult>? TryGetWebSearchResults(AgentResponseUpdate update, int maxResults = 5)
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
            if (results.Count >= maxResults)
            {
                break;
            }
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

/// <summary>One hit from an Unsloth server-side <c>web_search</c> tool call.</summary>
public sealed record WebSearchResult(string Title, string Url);
