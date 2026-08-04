using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;

namespace RedStar.Base;

public static class RedStarChatClientFactory
{
    public static IChatClient Create(RedStarOptions options, string modelId)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(modelId);

        var hasApiKey = !string.IsNullOrEmpty(options.ApiKey);

        var httpClient = new HttpClient(new ConditionalAuthHandler(stripAuthHeader: !hasApiKey));
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options.BaseUrl),
            Transport = new HttpClientPipelineTransport(httpClient),
        };

        var credential = new ApiKeyCredential(hasApiKey ? options.ApiKey : "not-needed");
        var openAiClient = new OpenAIClient(credential, clientOptions);
        return openAiClient.GetChatClient(modelId).AsIChatClient();
    }
}
