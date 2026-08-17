using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using RedStar.Base;
using RedStar.Base.Agents.GoogleAI;

namespace RedStar.UnitTest;

public class GoogleAIAgentFactoryTests
{
    [Fact]
    public void Create_Throws_WhenHttpClientIsNull()
    {
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        Assert.Throws<ArgumentNullException>(() => GoogleAIAgentFactory.Create(null!, options, "model"));
    }

    [Fact]
    public void Create_Throws_WhenOptionsIsNull()
    {
        var httpClient = new HttpClient();
        Assert.Throws<ArgumentNullException>(() => GoogleAIAgentFactory.Create(httpClient, null!, "model"));
    }

    [Fact]
    public void Create_Throws_WhenModelIdIsNullOrEmpty()
    {
        var httpClient = new HttpClient();
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        Assert.Throws<ArgumentException>(() => GoogleAIAgentFactory.Create(httpClient, options, ""));
    }

    [Fact]
    public void Create_ReturnsAgent_WithInstructionsSetFromParameter()
    {
        var httpClient = new HttpClient();
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        var agent = GoogleAIAgentFactory.Create(httpClient, options, "m", "be terse");

        var chatClientAgent = Assert.IsType<ChatClientAgent>(agent);
        Assert.Equal("be terse", chatClientAgent.Instructions);
    }

    [Fact]
    public void Create_ReturnsAgent_WithNullInstructions_WhenNoneProvided()
    {
        var httpClient = new HttpClient();
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        var agent = GoogleAIAgentFactory.Create(httpClient, options, "m");

        var chatClientAgent = Assert.IsType<ChatClientAgent>(agent);
        Assert.Null(chatClientAgent.Instructions);
    }

    [Fact]
    public async Task Create_BuiltAgent_SendsRequestToConfiguredEndpointAndModel()
    {
        var handler = new CapturingHandler();
        var options = new RedStarOptions
        {
            Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { BaseUrl = "https://generativelanguage.googleapis.com/openai/", ApiKey = "test-key" } },
        };

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/") };
        var agent = GoogleAIAgentFactory.Create(httpClient, options, "my-model", "be terse");

        await agent.RunAsync("hi");

        Assert.NotNull(handler.CapturedRequestUri);
        Assert.EndsWith("/chat/completions", handler.CapturedRequestUri!.ToString());
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
