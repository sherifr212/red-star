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
    /// <c>UnslothAgentFactory.Create</c>, there is no accompanying <c>CreateChatOptions</c>/<c>Patch</c>
    /// step -- LM Studio's chat completions endpoint needs no fields outside the standard OpenAI schema
    /// that the OpenAI SDK/<c>Microsoft.Extensions.AI</c> don't already model directly.
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

        var chatOptions = new ChatOptions { Instructions = instructions };

        return openAiClient.GetChatClient(modelId).AsAIAgent(new ChatClientAgentOptions { ChatOptions = chatOptions });
    }
}
