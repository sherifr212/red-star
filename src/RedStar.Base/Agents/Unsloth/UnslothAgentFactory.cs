using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using RedStar.Base.Telemetry;

namespace RedStar.Base.Agents.Unsloth;

public static class UnslothAgentFactory
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

        RedStarTelemetry.CreateLogger("RedStar.Base.Agents.Unsloth.UnslothAgentFactory")
            .LogInformation("Building chat agent for model {ModelId}", modelId);

        var unsloth = options.Agents.Unsloth;
        var hasApiKey = !string.IsNullOrEmpty(unsloth.ApiKey);

        var httpClient = new HttpClient(new ConditionalAuthHandler(stripAuthHeader: !hasApiKey));
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(unsloth.BaseUrl),
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        var credential = new ApiKeyCredential(hasApiKey ? unsloth.ApiKey : "not-needed");
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

        if (!options.Agents.Unsloth.WebSearchEnabled)
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
}
