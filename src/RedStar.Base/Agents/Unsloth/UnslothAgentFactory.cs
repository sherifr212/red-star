using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
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
    /// Builds an <see cref="AIAgent"/> backed by the Unsloth Studio server. <paramref name="httpClient"/> is
    /// the transport used for every request -- callers own its construction/lifetime (e.g. via
    /// <c>IHttpMessageHandlerFactory</c> wrapped in a <see cref="ConditionalAuthHandler"/>); this factory never
    /// constructs one itself. <paramref name="instructions"/> becomes the agent's system prompt (merged into
    /// <see cref="ChatOptions.Instructions"/> on every run by <see cref="ChatClientAgent"/>) rather than a
    /// message the caller has to manage.
    /// </summary>
    public static AIAgent Create(HttpClient httpClient, RedStarOptions options, string modelId, string? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(modelId);

        RedStarTelemetry.CreateLogger("RedStar.Base.Agents.Unsloth.UnslothAgentFactory")
            .LogBuildingAgent(modelId);

        var unsloth = options.Agents.Unsloth;
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(unsloth.BaseUrl),
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        var credential = new ApiKeyCredential(!string.IsNullOrEmpty(unsloth.ApiKey) ? unsloth.ApiKey : "not-needed");
        var openAiClient = new OpenAIClient(credential, clientOptions);

        var chatOptions = CreateChatOptions(options);
        chatOptions.Instructions = instructions;

        return openAiClient.GetChatClient(modelId).AsAIAgent(new ChatClientAgentOptions { ChatOptions = chatOptions });
    }

    /// <summary>
    /// Builds the <see cref="ChatOptions"/> to pass alongside each request. Always requests
    /// <c>stream_options.include_usage</c> via <see cref="ChatCompletionOptions.Patch"/> (the OpenAI SDK's
    /// <c>StreamOptions</c> property is internal, not modeled for external callers, same reason
    /// Unsloth's own fields below go through <c>Patch</c>) so a final <c>UsageContent</c> update carries the
    /// turn's output token count -- see <see cref="RedStar.Cli.ChatCommandHandler"/>'s per-block token/speed
    /// footer. Also applies Unsloth-specific fields via <c>Patch</c> when any tool is enabled (see
    /// <see cref="RedStarOptions.EnabledTools"/> on <see cref="UnslothAgentOptions"/>).
    /// </summary>
    public static ChatOptions CreateChatOptions(RedStarOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var completionOptions = new ChatCompletionOptions();
#pragma warning disable SCME0001 // Patch is an evaluation-only OpenAI SDK API for fields it doesn't model yet.
        completionOptions.Patch.Set("$.stream_options.include_usage"u8, true);

        var enabledTools = options.Agents.Unsloth.EnabledTools;
        if (enabledTools.Count > 0)
        {
            completionOptions.Patch.Set("$.enable_tools"u8, true);
            completionOptions.Patch.Set(
                "$.enabled_tools"u8, BinaryData.FromString(JsonSerializer.Serialize(enabledTools)));
        }
#pragma warning restore SCME0001

        return new ChatOptions { RawRepresentationFactory = _ => completionOptions };
    }
}
