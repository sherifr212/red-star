using System.Net;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

/// <summary>
/// Regression coverage for the single most-repeated correctness claim in this codebase's GoogleAI docs:
/// that a Gemini "thought signature" on a prior <see cref="FunctionCallContent"/> survives into the next
/// outgoing request verbatim, without RedStar needing to strip or re-derive it itself. Prior to this test
/// that claim rested entirely on reading the <c>Google.GenAI</c> SDK's source
/// (<c>GoogleGenAIChatClient.AddPartsForAIContents</c>) -- flagged in self-review as unverified against
/// any actual request. This exercises the real <see cref="Client"/>/<c>IChatClient</c> construction
/// <c>GoogleAIAgentFactory.Create</c> uses, the same way, and inspects the literal outgoing HTTP body via
/// the shared <see cref="FakeHttpMessageHandler"/> (single canned response is enough here since each test
/// only ever sends one request).
/// </summary>
public class GoogleAIThoughtSignaturePreservationTests
{
    private const string CannedResponseJson = """
        {
          "candidates": [
            {
              "content": { "role": "model", "parts": [{ "text": "It's sunny." }] },
              "finishReason": "STOP"
            }
          ]
        }
        """;

    private static readonly byte[] Signature = [1, 2, 3, 4];
    private static readonly string SignatureBase64 = Convert.ToBase64String(Signature);

    [Fact]
    public async Task PriorThoughtSignature_IsSentVerbatim_OnTheFollowUpRequest()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, CannedResponseJson);
        var chatClient = CreateChatClient(handler);

        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "what's the weather in NYC?"),
            new(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", "get_weather", new Dictionary<string, object?> { ["city"] = "NYC" }),
                new TextReasoningContent("") { ProtectedData = SignatureBase64 },
            ]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "sunny")]),
        };

        await chatClient.GetResponseAsync(history);

        var requestBody = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.Contains($"\"thoughtSignature\":\"{SignatureBase64}\"", requestBody);
    }

    [Fact]
    public async Task FunctionCall_WithNoPriorReasoning_StillSendsAValidRequest_UsingSkipValidationPlaceholder()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, CannedResponseJson);
        var chatClient = CreateChatClient(handler);

        // No TextReasoningContent at all -- represents IncludeThoughts = false, or a model turn that
        // never produced a thought. The SDK must still be able to send this function call back.
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "what's the weather in NYC?"),
            new(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_weather", new Dictionary<string, object?> { ["city"] = "NYC" })]),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", "sunny")]),
        };

        await chatClient.GetResponseAsync(history);

        var requestBody = await handler.LastRequest!.Content!.ReadAsStringAsync();
        Assert.Contains("\"thoughtSignature\":", requestBody);
        Assert.DoesNotContain($"\"thoughtSignature\":\"{SignatureBase64}\"", requestBody);
    }

    private static IChatClient CreateChatClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var client = new Client(
            apiKey: "test-key",
            httpOptions: new HttpOptions { BaseUrl = "https://generativelanguage.googleapis.com/" },
            clientOptions: new ClientOptions { HttpClientFactory = () => httpClient });
        return client.AsIChatClient("gemini-2.0-flash");
    }
}
