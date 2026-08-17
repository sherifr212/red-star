using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using RedStar.Base.Telemetry;

namespace RedStar.Base.Agents.GoogleAI;

/// <summary>
/// Builds an <see cref="AIAgent"/> backed by Google AI Studio via its OpenAI-compatible API.
/// Google AI Studio's `/v1/chat/completions` endpoint is OpenAI-compatible, allowing the use of
/// the standard OpenAI SDK without requiring Google-specific libraries. See
/// https://ai.google.dev/docs/gemini_api_docs/rest for API documentation.
/// </summary>
public static class GoogleAIAgentFactory
{
    /// <summary>
    /// Builds an <see cref="AIAgent"/> backed by Google AI Studio. <paramref name="instructions"/>
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
        var apiKey = googleAI.ApiKey;

        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException(
                "Google AI API key is required. Set it via --api-key, the " +
                "RedStar__Agents__GoogleAI__ApiKey environment variable, or appsettings.local.json.");
        }

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(googleAI.BaseUrl),
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        var credential = new ApiKeyCredential(apiKey);
        var openAiClient = new OpenAIClient(credential, clientOptions);

        var chatOptions = new ChatOptions { Instructions = instructions };

        return openAiClient.GetChatClient(modelId).AsAIAgent(
            new ChatClientAgentOptions { ChatOptions = chatOptions });
    }
}
