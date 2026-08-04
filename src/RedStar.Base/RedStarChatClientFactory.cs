using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

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
}
