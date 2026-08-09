using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using RedStar.Base.Telemetry;

namespace RedStar.Base.Agents.LMStudio;

public static class LMStudioAgentFactory
{
    /// <summary>
    /// Builds an <see cref="AIAgent"/> backed by an LM Studio local server. <paramref name="instructions"/>
    /// becomes the agent's system prompt (merged into <see cref="ChatOptions.Instructions"/> on every run
    /// by <see cref="ChatClientAgent"/>) rather than a message the caller has to manage. Unlike
    /// <c>UnslothAgentFactory.Create</c>, there is no Unsloth-style <c>enable_tools</c>/<c>enabled_tools</c>
    /// request customization -- LM Studio's chat completions endpoint needs no fields outside the standard
    /// OpenAI schema that the OpenAI SDK/<c>Microsoft.Extensions.AI</c> don't already model directly. See
    /// <see cref="CreateChatOptions"/> for the one request field it does add.
    /// </summary>
    public static AIAgent Create(RedStarOptions options, string modelId, string? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(modelId);

        RedStarTelemetry.CreateLogger("RedStar.Base.Agents.LMStudio.LMStudioAgentFactory")
            .LogInformation("Building chat agent for model {ModelId}", modelId);

        var lmStudio = options.Agents.LMStudio;
        var hasApiKey = !string.IsNullOrEmpty(lmStudio.ApiKey);

        var httpClient = new HttpClient(new ConditionalAuthHandler(stripAuthHeader: !hasApiKey));
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(lmStudio.BaseUrl),
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        var credential = new ApiKeyCredential(hasApiKey ? lmStudio.ApiKey : "not-needed");
        var openAiClient = new OpenAIClient(credential, clientOptions);

        var chatOptions = CreateChatOptions();
        chatOptions.Instructions = instructions;

        return openAiClient.GetChatClient(modelId).AsAIAgent(new ChatClientAgentOptions { ChatOptions = chatOptions });
    }

    /// <summary>
    /// Builds the <see cref="ChatOptions"/> to pass alongside each request. Always requests
    /// <c>stream_options.include_usage</c> via <see cref="ChatCompletionOptions.Patch"/> (the OpenAI SDK's
    /// <c>StreamOptions</c> property is internal, not modeled for external callers, same reason Unsloth's
    /// own fields go through <c>Patch</c> in <c>UnslothAgentFactory.CreateChatOptions</c>) so a final
    /// <c>UsageContent</c> update carries the turn's output token count -- see
    /// <see cref="RedStar.Cli.ChatCommandHandler"/>'s per-block token/speed footer.
    /// </summary>
    public static ChatOptions CreateChatOptions()
    {
        var completionOptions = new ChatCompletionOptions();
#pragma warning disable SCME0001 // Patch is an evaluation-only OpenAI SDK API for fields it doesn't model yet.
        completionOptions.Patch.Set("$.stream_options.include_usage"u8, true);
#pragma warning restore SCME0001

        return new ChatOptions { RawRepresentationFactory = _ => completionOptions };
    }
}
