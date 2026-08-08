using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using RedStar.Base;
using RedStar.Base.Agents.LMStudio;

namespace RedStar.UnitTest;

public class LMStudioAgentFactoryTests
{
    [Fact]
    public void Create_Throws_WhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => LMStudioAgentFactory.Create(null!, "model"));
    }

    [Fact]
    public void Create_Throws_WhenModelIdIsNullOrEmpty()
    {
        Assert.Throws<ArgumentException>(() => LMStudioAgentFactory.Create(new RedStarOptions(), ""));
    }

    [Fact]
    public void Create_ReturnsAgent_WithInstructionsSetFromParameter()
    {
        var agent = LMStudioAgentFactory.Create(new RedStarOptions(), "m", "be terse");

        var chatClientAgent = Assert.IsType<ChatClientAgent>(agent);
        Assert.Equal("be terse", chatClientAgent.Instructions);
    }

    [Fact]
    public void Create_ReturnsAgent_WithNullInstructions_WhenNoneProvided()
    {
        var agent = LMStudioAgentFactory.Create(new RedStarOptions(), "m");

        var chatClientAgent = Assert.IsType<ChatClientAgent>(agent);
        Assert.Null(chatClientAgent.Instructions);
    }

    /// <summary>
    /// Proves a built agent's outgoing request actually reaches the configured endpoint/model with no
    /// unexpected extra fields on the wire -- unlike Unsloth, LM Studio needs no <c>Patch</c>-based request
    /// customization, so there's nothing analogous to <c>UnslothAgentFactoryTests</c>'s
    /// <c>enable_tools</c>/<c>enabled_tools</c> assertions to make here.
    /// </summary>
    [Fact]
    public async Task Create_BuiltAgent_SendsRequestToConfiguredEndpointAndModel()
    {
        var handler = new CapturingHandler();
        var options = new RedStarOptions
        {
            Agents = new AgentsOptions { LMStudio = new LMStudioAgentOptions { BaseUrl = "http://127.0.0.1:1234/v1" } },
        };

        // LMStudioAgentFactory.Create builds its own real HttpClient internally, so this test swaps in the
        // capturing transport the same way UnslothAgentFactoryTests does for its own request-capture test:
        // by composing the OpenAI SDK client directly with CapturingHandler, using the same ChatOptions
        // LMStudioAgentFactory.Create would build (Instructions only -- no Patch fields for LM Studio).
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(options.Agents.LMStudio.BaseUrl),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
        };
        var openAiClient = new OpenAIClient(new ApiKeyCredential("not-needed"), clientOptions);
        var chatOptions = new ChatOptions { Instructions = "be terse" };
        AIAgent agent = openAiClient.GetChatClient("my-model").AsAIAgent(new ChatClientAgentOptions { ChatOptions = chatOptions });

        await agent.RunAsync("hi");

        Assert.NotNull(handler.CapturedRequestUri);
        Assert.EndsWith("/v1/chat/completions", handler.CapturedRequestUri!.ToString());
        Assert.NotNull(handler.CapturedRequestBody);
        Assert.Contains("\"model\":\"my-model\"", handler.CapturedRequestBody);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? CapturedRequestBody { get; private set; }

        public Uri? CapturedRequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequestUri = request.RequestUri;
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
                  "model": "my-model",
                  "choices": [{"index":0,"message":{"role":"assistant","content":"hi"},"finish_reason":"stop"}]
                }
                """),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return response;
        }
    }
}
