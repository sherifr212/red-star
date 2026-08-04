using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using RedStar.Base;

namespace RedStar.UnitTest;

public class RedStarChatClientFactoryTests
{
    [Fact]
    public void Create_Throws_WhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => RedStarChatClientFactory.Create(null!, "model"));
    }

    [Fact]
    public void Create_Throws_WhenModelIdIsNullOrEmpty()
    {
        Assert.Throws<ArgumentException>(() => RedStarChatClientFactory.Create(new RedStarOptions(), ""));
    }

    [Fact]
    public void Create_ReturnsAgent_WithInstructionsSetFromParameter()
    {
        var agent = RedStarChatClientFactory.Create(new RedStarOptions(), "m", "be terse");

        var chatClientAgent = Assert.IsType<ChatClientAgent>(agent);
        Assert.Equal("be terse", chatClientAgent.Instructions);
    }

    [Fact]
    public void Create_ReturnsAgent_WithNullInstructions_WhenNoneProvided()
    {
        var agent = RedStarChatClientFactory.Create(new RedStarOptions(), "m");

        var chatClientAgent = Assert.IsType<ChatClientAgent>(agent);
        Assert.Null(chatClientAgent.Instructions);
    }

    [Fact]
    public void CreateChatOptions_ReturnsNull_WhenWebSearchDisabled()
    {
        var options = new RedStarOptions { WebSearchEnabled = false };

        var chatOptions = RedStarChatClientFactory.CreateChatOptions(options);

        Assert.Null(chatOptions);
    }

    [Fact]
    public void CreateChatOptions_ReturnsOptionsWithRawRepresentationFactory_WhenWebSearchEnabled()
    {
        var options = new RedStarOptions { WebSearchEnabled = true };

        var chatOptions = RedStarChatClientFactory.CreateChatOptions(options);

        Assert.NotNull(chatOptions);
        Assert.NotNull(chatOptions!.RawRepresentationFactory);
    }

    [Fact]
    public void CreateChatOptions_RawRepresentation_IsChatCompletionOptionsWithOnlyWebSearchEnabled()
    {
        var options = new RedStarOptions { WebSearchEnabled = true };
        var chatOptions = RedStarChatClientFactory.CreateChatOptions(options);

        var raw = chatOptions!.RawRepresentationFactory!(null!);
        var completionOptions = Assert.IsType<ChatCompletionOptions>(raw);

        var json = ModelReaderWriter.Write(completionOptions, ModelReaderWriterOptions.Json).ToString();

        Assert.Contains("\"enable_tools\":true", json);
        Assert.Contains("\"enabled_tools\":[\"web_search\"]", json);
    }

    [Fact]
    public void CreateChatOptions_Throws_WhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => RedStarChatClientFactory.CreateChatOptions(null!));
    }

    /// <summary>
    /// The other tests here only check that the raw <see cref="ChatCompletionOptions"/> serializes correctly
    /// in isolation. This one proves the <c>Patch</c> fields actually survive the real
    /// <c>IChatClient</c>/OpenAI SDK request pipeline onto the wire, rather than being dropped or overwritten
    /// before the HTTP call is made.
    /// </summary>
    [Fact]
    public async Task CreateChatOptions_EnableToolsFields_ReachTheOutgoingHttpRequestBody()
    {
        var handler = new CapturingHandler();
        var httpClient = new HttpClient(handler);
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:8888/v1"),
            Transport = new HttpClientPipelineTransport(httpClient),
        };
        var openAiClient = new OpenAIClient(new ApiKeyCredential("test"), clientOptions);
        IChatClient chatClient = openAiClient.GetChatClient("m").AsIChatClient();

        var chatOptions = RedStarChatClientFactory.CreateChatOptions(new RedStarOptions { WebSearchEnabled = true });

        await chatClient.GetResponseAsync(
            [new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, "hi")], chatOptions);

        Assert.NotNull(handler.CapturedRequestBody);
        Assert.Contains("\"enable_tools\":true", handler.CapturedRequestBody);
        Assert.Contains("\"enabled_tools\":[\"web_search\"]", handler.CapturedRequestBody);
    }

    /// <summary>
    /// Proves the same Patch fields survive when composed the way <see cref="RedStarChatClientFactory.Create"/>
    /// actually composes them: as the agent's default <c>ChatOptions</c> via <c>AsAIAgent</c>/
    /// <see cref="ChatClientAgentOptions"/>, rather than passed alongside a raw <c>IChatClient</c> call.
    /// </summary>
    [Fact]
    public async Task Create_BuiltAgent_PutsUnslothPatchFields_OnTheOutgoingRequest()
    {
        var handler = new CapturingHandler();
        var httpClient = new HttpClient(handler);
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri("http://127.0.0.1:8888/v1"),
            Transport = new HttpClientPipelineTransport(httpClient),
        };
        var openAiClient = new OpenAIClient(new ApiKeyCredential("test"), clientOptions);

        var chatOptions = RedStarChatClientFactory.CreateChatOptions(new RedStarOptions { WebSearchEnabled = true })!;
        AIAgent agent = openAiClient.GetChatClient("m").AsAIAgent(new ChatClientAgentOptions { ChatOptions = chatOptions });

        await agent.RunAsync("hi");

        Assert.NotNull(handler.CapturedRequestBody);
        Assert.Contains("\"enable_tools\":true", handler.CapturedRequestBody);
        Assert.Contains("\"enabled_tools\":[\"web_search\"]", handler.CapturedRequestBody);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? CapturedRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "id": "chatcmpl-1",
                  "object": "chat.completion",
                  "created": 0,
                  "model": "m",
                  "choices": [{"index":0,"message":{"role":"assistant","content":"hi"},"finish_reason":"stop"}]
                }
                """),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return response;
        }
    }
}
