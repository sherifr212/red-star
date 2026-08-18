using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RedStar.Base.Telemetry;

namespace RedStar.Base.Agents.GoogleAI;

/// <summary>
/// Builds an <see cref="AIAgent"/> backed by Gemini via the native <c>Google.GenAI</c> SDK
/// (<see cref="Client.AsIChatClient"/>), not an OpenAI-compatible shim. Microsoft Agent Framework's
/// OpenAI-shaped abstractions don't cleanly cover Gemini/Gemma-family quirks -- most notably
/// "thinking mode" reasoning output getting lost or not coming back as a distinct block -- because an
/// OpenAI-compatible endpoint can't express Gemini-native concepts like <c>ThinkingConfig</c>. The
/// <c>Google.GenAI</c> SDK's own <c>IChatClient</c> implementation maps <see cref="ChatOptions.Reasoning"/>
/// directly onto Gemini's <c>ThinkingConfig</c> and emits thought text as a distinct
/// <c>TextReasoningContent</c> (vs. plain <c>TextContent</c> for the answer) -- picked up generically by
/// <c>RedStar.Cli.ChatEngine</c>'s existing <c>TextReasoningContent</c> handling with no extra wiring here.
/// </summary>
public static class GoogleAIAgentFactory
{
    /// <summary>
    /// Builds an <see cref="AIAgent"/> backed by Gemini. <paramref name="httpClient"/> is the transport
    /// used for every request -- callers own its construction/lifetime (typically a named
    /// <see cref="IHttpClientFactory"/> client, same pipeline as the other HTTP-based agents; unlike
    /// Unsloth/LMStudio, no <see cref="ConditionalAuthHandler"/> is needed since Gemini always requires a
    /// real API key and the SDK sets its own <c>x-goog-api-key</c> header). <paramref name="instructions"/>
    /// becomes the agent's system prompt (merged into <see cref="ChatOptions.Instructions"/> on every run
    /// by <see cref="ChatClientAgent"/>) rather than a message the caller has to manage.
    /// </summary>
    public static AIAgent Create(HttpClient httpClient, RedStarOptions options, string modelId, string? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(modelId);

        RedStarTelemetry.CreateLogger("RedStar.Base.Agents.GoogleAI.GoogleAIAgentFactory")
            .LogBuildingAgent(modelId);

        var googleAI = options.Agents.GoogleAI;

        if (string.IsNullOrEmpty(googleAI.ApiKey))
        {
            throw new InvalidOperationException(
                "Google AI API key is required. Set it via --api-key, the " +
                "RedStar__Agents__GoogleAI__ApiKey environment variable, or appsettings.local.json.");
        }

        var client = new Client(
            apiKey: googleAI.ApiKey,
            httpOptions: new HttpOptions { BaseUrl = googleAI.BaseUrl },
            clientOptions: new ClientOptions { HttpClientFactory = () => httpClient });

        var chatOptions = CreateChatOptions(options);
        chatOptions.Instructions = instructions;

        return client.AsIChatClient(modelId).AsAIAgent(new ChatClientAgentOptions { ChatOptions = chatOptions });
    }

    /// <summary>
    /// Builds the <see cref="ChatOptions"/> to pass alongside each request. Maps
    /// <see cref="GoogleAIAgentOptions.ThinkingEffort"/>/<see cref="GoogleAIAgentOptions.IncludeThoughts"/>
    /// onto <see cref="ChatOptions.Reasoning"/>, which the <c>Google.GenAI</c> SDK's <c>IChatClient</c>
    /// translates into Gemini's <c>ThinkingConfig</c> (<c>ThinkingBudget</c>/<c>ThinkingLevel</c> from
    /// <c>Effort</c>, <c>IncludeThoughts</c> from <c>Output</c>). Left as <c>null</c> only when
    /// <see cref="GoogleAIAgentOptions.ThinkingEffort"/> is blank/unrecognized and
    /// <see cref="GoogleAIAgentOptions.IncludeThoughts"/> is <c>false</c> -- i.e. only when there is
    /// nothing to configure -- so the model's own default thinking behavior applies untouched.
    /// </summary>
    public static ChatOptions CreateChatOptions(RedStarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var googleAI = options.Agents.GoogleAI;
        var chatOptions = new ChatOptions();

        ReasoningEffort? effort = null;
        if (!string.IsNullOrWhiteSpace(googleAI.ThinkingEffort) &&
            Enum.TryParse<ReasoningEffort>(googleAI.ThinkingEffort, ignoreCase: true, out var parsedEffort))
        {
            effort = parsedEffort;
        }

        if (effort is not null || googleAI.IncludeThoughts)
        {
            chatOptions.Reasoning = new ReasoningOptions
            {
                Effort = effort,
                Output = googleAI.IncludeThoughts ? ReasoningOutput.Summary : ReasoningOutput.None,
            };
        }

        return chatOptions;
    }
}
