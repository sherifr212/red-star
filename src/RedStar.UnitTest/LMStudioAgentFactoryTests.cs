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
        Assert.Throws<ArgumentNullException>(() => LMStudioAgentFactory.Create(new HttpClient(), null!, "model"));
    }

    [Fact]
    public void Create_Throws_WhenModelIdIsNullOrEmpty()
    {
        Assert.Throws<ArgumentException>(() => LMStudioAgentFactory.Create(new HttpClient(), new RedStarOptions(), ""));
    }

    [Fact]
    public void Create_ReturnsAgent_WithInstructionsSetFromParameter()
    {
        var agent = LMStudioAgentFactory.Create(new HttpClient(), new RedStarOptions(), "m", "be terse");

        var chatClientAgent = Assert.IsType<ChatClientAgent>(agent);
        Assert.Equal("be terse", chatClientAgent.Instructions);
    }

    [Fact]
    public void Create_ReturnsAgent_WithNullInstructions_WhenNoneProvided()
    {
        var agent = LMStudioAgentFactory.Create(new HttpClient(), new RedStarOptions(), "m");

        var chatClientAgent = Assert.IsType<ChatClientAgent>(agent);
        Assert.Null(chatClientAgent.Instructions);
    }

    /// <summary>
    /// Proves a built agent's outgoing request actually reaches the configured endpoint/model, and carries
    /// <c>stream_options.include_usage</c> (see <see cref="LMStudioAgentFactory.CreateChatOptions"/>) but no
    /// Unsloth-only fields.
    /// </summary>
    [Fact]
    public async Task Create_BuiltAgent_SendsRequestToConfiguredEndpointAndModel()
    {
        var handler = new CapturingHandler();
        var options = new RedStarOptions
        {
            Agents = new AgentsOptions { LMStudio = new LMStudioAgentOptions { BaseUrl = "http://127.0.0.1:1234/v1" } },
        };

        var httpClient = new HttpClient(handler);
        var agent = LMStudioAgentFactory.Create(httpClient, options, "my-model", "be terse");

        await agent.RunAsync("hi");

        Assert.NotNull(handler.CapturedRequestUri);
        Assert.EndsWith("/v1/chat/completions", handler.CapturedRequestUri!.ToString());
        Assert.NotNull(handler.CapturedRequestBody);
        Assert.Contains("\"model\":\"my-model\"", handler.CapturedRequestBody);
        Assert.Contains("\"stream_options\":{\"include_usage\":true}", handler.CapturedRequestBody);
        Assert.DoesNotContain("enable_tools", handler.CapturedRequestBody);
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