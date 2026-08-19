using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RedStar.Base;
using RedStar.Base.Agents.GoogleAI;
using System.Net;
using System.Net.Http.Headers;

namespace RedStar.UnitTest;

public class GoogleAIAgentFactoryTests
{
    [Fact]
    public void Create_Throws_WhenHttpClientFactoryIsNull()
    {
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        Assert.Throws<ArgumentNullException>(() => GoogleAIAgentFactory.Create(null!, options, "model"));
    }

    [Fact]
    public void Create_Throws_WhenOptionsIsNull()
    {
        var httpClient = new HttpClient();
        Assert.Throws<ArgumentNullException>(() => GoogleAIAgentFactory.Create(() => httpClient, null!, "model"));
    }

    [Fact]
    public void Create_Throws_WhenModelIdIsNullOrEmpty()
    {
        var httpClient = new HttpClient();
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        Assert.Throws<ArgumentException>(() => GoogleAIAgentFactory.Create(() => httpClient, options, ""));
    }

    [Fact]
    public void Create_Throws_WhenApiKeyIsMissing()
    {
        var httpClient = new HttpClient();
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "" } } };
        Assert.Throws<InvalidOperationException>(() => GoogleAIAgentFactory.Create(() => httpClient, options, "model"));
    }

    [Fact]
    public void Create_ReturnsAgent_WithInstructionsSetFromParameter()
    {
        var httpClient = new HttpClient();
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        var agent = GoogleAIAgentFactory.Create(() => httpClient, options, "m", "be terse");

        var chatClientAgent = Assert.IsType<ChatClientAgent>(agent);
        Assert.Equal("be terse", chatClientAgent.Instructions);
    }

    [Fact]
    public void Create_ReturnsAgent_WithNullInstructions_WhenNoneProvided()
    {
        var httpClient = new HttpClient();
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        var agent = GoogleAIAgentFactory.Create(() => httpClient, options, "m");

        var chatClientAgent = Assert.IsType<ChatClientAgent>(agent);
        Assert.Null(chatClientAgent.Instructions);
    }

    [Fact]
    public async Task Create_BuiltAgent_SendsRequestToConfiguredEndpointAndModel()
    {
        var handler = new CapturingHandler();
        var options = new RedStarOptions
        {
            Agents = new AgentsOptions
            {
                GoogleAI = new GoogleAIAgentOptions
                {
                    BaseUrl = "https://generativelanguage.googleapis.com/",
                    ApiKey = "test-key",
                },
            },
        };

        var httpClient = new HttpClient(handler);
        var agent = GoogleAIAgentFactory.Create(() => httpClient, options, "my-model", "be terse");

        await agent.RunAsync("hi");

        Assert.NotNull(handler.CapturedRequestUri);
        Assert.Contains("my-model", handler.CapturedRequestUri!.ToString());
        Assert.Contains(":generateContent", handler.CapturedRequestUri!.ToString());
        Assert.StartsWith("https://generativelanguage.googleapis.com/", handler.CapturedRequestUri!.ToString());
        Assert.NotNull(handler.CapturedRequestBody);
        Assert.Contains("\"hi\"", handler.CapturedRequestBody);
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
                  "candidates": [
                    {
                      "content": {"role": "model", "parts": [{"text": "hi there"}]},
                      "finishReason": "STOP"
                    }
                  ]
                }
                """),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return response;
        }
    }

    [Fact]
    public void CreateChatOptions_Throws_WhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => GoogleAIAgentFactory.CreateChatOptions(null!));
    }

    [Fact]
    public void CreateChatOptions_SetsReasoningOutputOnly_WhenThinkingEffortBlank_AndIncludeThoughtsDefaultTrue()
    {
        var options = WithGoogleAI();
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.NotNull(chatOptions.Reasoning);
        Assert.Null(chatOptions.Reasoning!.Effort);
        Assert.Equal(ReasoningOutput.Summary, chatOptions.Reasoning.Output);
    }

    [Fact]
    public void CreateChatOptions_SetsReasoningEffort_WhenThinkingEffortConfigured()
    {
        var options = WithGoogleAI(thinkingEffort: "Low");
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.NotNull(chatOptions.Reasoning);
        Assert.Equal(ReasoningEffort.Low, chatOptions.Reasoning!.Effort);
    }

    [Fact]
    public void CreateChatOptions_MatchesThinkingEffort_CaseInsensitively()
    {
        var options = WithGoogleAI(thinkingEffort: "hIgH");
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Equal(ReasoningEffort.High, chatOptions.Reasoning!.Effort);
    }

    [Fact]
    public void CreateChatOptions_MatchesThinkingEffort_WhenPaddedWithWhitespace()
    {
        // A copy-pasted/env-quoted value like " High " must still resolve by name -- rejecting
        // numeric strings (see the test below) must not regress this.
        var options = WithGoogleAI(thinkingEffort: "  High  ");
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Equal(ReasoningEffort.High, chatOptions.Reasoning!.Effort);
    }

    [Fact]
    public void CreateChatOptions_LeavesEffortNull_WhenThinkingEffortUnrecognized()
    {
        var options = WithGoogleAI(thinkingEffort: "bogus");
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Null(chatOptions.Reasoning!.Effort);
    }

    [Fact]
    public void CreateChatOptions_LeavesEffortNull_WhenThinkingEffortIsANumericString()
    {
        // Enum.TryParse<T> parses numeric strings regardless of whether they name a real member --
        // "3"/"47" must be rejected the same as any other unrecognized value, not silently accepted
        // as ReasoningEffort.High or an undefined enum value.
        var options = WithGoogleAI(thinkingEffort: "3");
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Null(chatOptions.Reasoning!.Effort);
    }

    [Fact]
    public void CreateChatOptions_SetsReasoningOutputNone_WhenIncludeThoughtsFalse()
    {
        var options = WithGoogleAI(thinkingEffort: "Low", includeThoughts: false);
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.NotNull(chatOptions.Reasoning);
        Assert.Equal(ReasoningOutput.None, chatOptions.Reasoning!.Output);
    }

    [Fact]
    public void CreateChatOptions_LeavesReasoningNull_WhenNoThinkingEffortAndIncludeThoughtsFalse()
    {
        var options = WithGoogleAI(includeThoughts: false, thinkingEffort: "");
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Null(chatOptions.Reasoning);
    }

    [Fact]
    public void CreateChatOptions_AppliesDefaultInferenceParameters_FromNewGoogleAIAgentOptions()
    {
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Equal(1.0f, chatOptions.Temperature);
        Assert.Equal(0.95f, chatOptions.TopP);
        Assert.Equal(40, chatOptions.TopK);
        Assert.Equal(8192, chatOptions.MaxOutputTokens);
        Assert.Equal(0.0f, chatOptions.FrequencyPenalty);
        Assert.Equal(0.0f, chatOptions.PresencePenalty);
        Assert.Null(chatOptions.Seed);
        Assert.Null(chatOptions.StopSequences);
    }

    [Fact]
    public void CreateChatOptions_CarriesConfiguredInferenceParameters_OntoChatOptions()
    {
        var options = new RedStarOptions
        {
            Agents = new AgentsOptions
            {
                GoogleAI = new GoogleAIAgentOptions
                {
                    ApiKey = "test-key",
                    Temperature = 0.2,
                    TopP = 0.5,
                    TopK = 10,
                    MaxOutputTokens = 512,
                    FrequencyPenalty = 0.3,
                    PresencePenalty = 0.4,
                    Seed = 42,
                    StopSequences = ["STOP", "END"],
                },
            },
        };

        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Equal(0.2f, chatOptions.Temperature);
        Assert.Equal(0.5f, chatOptions.TopP);
        Assert.Equal(10, chatOptions.TopK);
        Assert.Equal(512, chatOptions.MaxOutputTokens);
        Assert.Equal(0.3f, chatOptions.FrequencyPenalty);
        Assert.Equal(0.4f, chatOptions.PresencePenalty);
        Assert.Equal(42, chatOptions.Seed);
        Assert.Equal(["STOP", "END"], chatOptions.StopSequences);
    }

    [Fact]
    public void CreateChatOptions_LeavesInferenceParametersNull_WhenConfiguredAsNull()
    {
        var options = new RedStarOptions
        {
            Agents = new AgentsOptions
            {
                GoogleAI = new GoogleAIAgentOptions
                {
                    ApiKey = "test-key",
                    Temperature = null,
                    TopP = null,
                    TopK = null,
                    MaxOutputTokens = null,
                    FrequencyPenalty = null,
                    PresencePenalty = null,
                },
            },
        };

        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Null(chatOptions.Temperature);
        Assert.Null(chatOptions.TopP);
        Assert.Null(chatOptions.TopK);
        Assert.Null(chatOptions.MaxOutputTokens);
        Assert.Null(chatOptions.FrequencyPenalty);
        Assert.Null(chatOptions.PresencePenalty);
    }

    [Fact]
    public void CreateChatOptions_LeavesToolsNull_WhenNoToolsProvided()
    {
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Null(chatOptions.Tools);
    }

    [Fact]
    public void CreateChatOptions_LeavesToolsNull_WhenToolsListIsEmpty()
    {
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options, tools: []);

        Assert.Null(chatOptions.Tools);
    }

    [Fact]
    public void CreateChatOptions_CarriesTools_OntoChatOptionsTools_WhenProvided()
    {
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        var tool = AIFunctionFactory.Create(() => "ok", name: "test_tool");

        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options, tools: [tool]);

        Assert.NotNull(chatOptions.Tools);
        Assert.Single(chatOptions.Tools);
        Assert.Same(tool, chatOptions.Tools[0]);
    }

    [Fact]
    public void Create_ReturnsAgent_WhenToolsProvided_WithoutThrowing()
    {
        var httpClient = new HttpClient();
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };
        var tool = AIFunctionFactory.Create(() => "ok", name: "test_tool");

        var agent = GoogleAIAgentFactory.Create(() => httpClient, options, "m", tools: [tool]);

        Assert.IsType<ChatClientAgent>(agent);
    }

    [Fact]
    public void Create_ReturnsAgent_WhenToolsIsEmpty()
    {
        var httpClient = new HttpClient();
        var options = new RedStarOptions { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { ApiKey = "test-key" } } };

        var agent = GoogleAIAgentFactory.Create(() => httpClient, options, "m", tools: []);

        Assert.IsType<ChatClientAgent>(agent);
    }

    [Fact]
    public void CreateChatOptions_LeavesToolsAndRawRepresentationFactoryNull_WhenNoHostedToolsEnabled()
    {
        var options = WithHostedTools();
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Null(chatOptions.Tools);
        Assert.Null(chatOptions.RawRepresentationFactory);
    }

    [Fact]
    public void CreateChatOptions_AddsHostedWebSearchTool_WhenGoogleSearchEnabled()
    {
        var options = WithHostedTools(googleSearch: true);
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.NotNull(chatOptions.Tools);
        Assert.Single(chatOptions.Tools);
        Assert.IsType<HostedWebSearchTool>(chatOptions.Tools[0]);
    }

    [Fact]
    public void CreateChatOptions_AddsHostedCodeInterpreterTool_WhenCodeExecutionEnabled()
    {
        var options = WithHostedTools(codeExecution: true);
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.NotNull(chatOptions.Tools);
        Assert.Single(chatOptions.Tools);
        Assert.IsType<HostedCodeInterpreterTool>(chatOptions.Tools[0]);
    }

    [Fact]
    public void CreateChatOptions_SetsRawRepresentationFactory_WhenUrlContextEnabled()
    {
        var options = WithHostedTools(urlContext: true);
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.NotNull(chatOptions.RawRepresentationFactory);
        var raw = Assert.IsType<Google.GenAI.Types.GenerateContentConfig>(chatOptions.RawRepresentationFactory!(null!));
        Assert.NotNull(raw.Tools);
        var tool = Assert.Single(raw.Tools);
        Assert.NotNull(tool.UrlContext);
    }

    [Fact]
    public void CreateChatOptions_UrlContextEnabled_LeavesChatOptionsToolsNull_WhenNoOtherToolsPresent()
    {
        var options = WithHostedTools(urlContext: true);
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Null(chatOptions.Tools);
    }

    [Fact]
    public void CreateChatOptions_RawRepresentationFactory_ReturnsAFreshToolsListEachCall()
    {
        // ChatOptions (and its RawRepresentationFactory delegate) is built once per agent and reused
        // for every turn of a session. The Google.GenAI SDK mutates the GenerateContentConfig.Tools
        // list returned by this factory in place (appending ChatOptions.Tools-derived entries) --
        // if the delegate closed over one shared list, that mutation from turn 1 would still be
        // present (and re-mutated) on turn 2, growing the tool list without bound. Simulate the SDK's
        // in-place mutation here and assert the next call starts from an unmutated list again.
        var options = WithHostedTools(urlContext: true);
        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        var firstCall = Assert.IsType<Google.GenAI.Types.GenerateContentConfig>(
            chatOptions.RawRepresentationFactory!(null!));
        firstCall.Tools!.Add(new Google.GenAI.Types.Tool());
        Assert.Equal(2, firstCall.Tools!.Count);

        var secondCall = Assert.IsType<Google.GenAI.Types.GenerateContentConfig>(
            chatOptions.RawRepresentationFactory!(null!));

        Assert.NotSame(firstCall.Tools, secondCall.Tools);
        Assert.Single(secondCall.Tools!);
    }

    [Fact]
    public void CreateChatOptions_CombinesAllHostedTools_WithClientInjectedTools()
    {
        var options = WithHostedTools(googleSearch: true, codeExecution: true, urlContext: true);
        var clientTool = AIFunctionFactory.Create(() => "ok", name: "test_tool");

        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options, tools: [clientTool]);

        Assert.NotNull(chatOptions.Tools);
        Assert.Equal(3, chatOptions.Tools.Count);
        Assert.Contains(chatOptions.Tools, t => t is HostedWebSearchTool);
        Assert.Contains(chatOptions.Tools, t => t is HostedCodeInterpreterTool);
        Assert.Contains(chatOptions.Tools, t => ReferenceEquals(t, clientTool));
        Assert.NotNull(chatOptions.RawRepresentationFactory);
    }

    [Fact]
    public void CreateChatOptions_IgnoresUnrecognizedHostedToolKey()
    {
        var options = WithHostedTools();
        options.Agents.GoogleAI.HostedTools["SomeFutureTool"] = true;

        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.Null(chatOptions.Tools);
    }

    [Fact]
    public void Create_ReturnsAgent_WhenHostedToolsEnabled_WithoutThrowing()
    {
        var httpClient = new HttpClient();
        var options = WithHostedTools(googleSearch: true, codeExecution: true, urlContext: true);

        var agent = GoogleAIAgentFactory.Create(() => httpClient, options, "m");

        Assert.IsType<ChatClientAgent>(agent);
    }

    [Fact]
    public void GoogleAIAgentOptions_HostedTools_DefaultsToEveryKnownToolDisabled()
    {
        var hostedTools = new GoogleAIAgentOptions().HostedTools;

        Assert.Equal(GoogleAIHostedTools.Known.Count, hostedTools.Count);
        foreach (var name in GoogleAIHostedTools.Known)
        {
            Assert.False(hostedTools[name]);
        }
    }

    [Fact]
    public void GoogleAIAgentOptions_HostedTools_DefaultLookupIsCaseInsensitive()
    {
        var hostedTools = new GoogleAIAgentOptions().HostedTools;

        Assert.True(hostedTools.ContainsKey("googlesearch"));
        Assert.True(hostedTools.ContainsKey("GOOGLESEARCH"));
        Assert.False(hostedTools["gOoGlEsEaRcH"]);
    }

    [Fact]
    public void CreateChatOptions_HostedToolLookup_IsCaseInsensitive_EvenWithCustomDictionary()
    {
        var options = new RedStarOptions
        {
            Agents = new AgentsOptions
            {
                GoogleAI = new GoogleAIAgentOptions
                {
                    ApiKey = "test-key",
                    HostedTools = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["googlesearch"] = true,
                    },
                },
            },
        };

        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.NotNull(chatOptions.Tools);
        Assert.IsType<HostedWebSearchTool>(Assert.Single(chatOptions.Tools));
    }

    [Fact]
    public void CreateChatOptions_DeduplicatesHostedTool_WhenCallerDictionaryHasMismatchedCaseDuplicateKeys()
    {
        // A hand-built ordinal-comparer dictionary can hold "GoogleSearch" and "googlesearch" as two
        // separate true entries -- something normal config binding (case-insensitive by construction,
        // see the test above) can never produce. CreateChatOptions must still only add the tool once.
        var options = new RedStarOptions
        {
            Agents = new AgentsOptions
            {
                GoogleAI = new GoogleAIAgentOptions
                {
                    ApiKey = "test-key",
                    HostedTools = new Dictionary<string, bool>(StringComparer.Ordinal)
                    {
                        ["GoogleSearch"] = true,
                        ["googlesearch"] = true,
                    },
                },
            },
        };

        var chatOptions = GoogleAIAgentFactory.CreateChatOptions(options);

        Assert.NotNull(chatOptions.Tools);
        Assert.IsType<HostedWebSearchTool>(Assert.Single(chatOptions.Tools));
    }

    private static RedStarOptions WithHostedTools(
        bool googleSearch = false, bool codeExecution = false, bool urlContext = false) =>
        new()
        {
            Agents = new AgentsOptions
            {
                GoogleAI = new GoogleAIAgentOptions
                {
                    ApiKey = "test-key",
                    HostedTools = new Dictionary<string, bool>
                    {
                        [GoogleAIHostedTools.GoogleSearch] = googleSearch,
                        [GoogleAIHostedTools.CodeExecution] = codeExecution,
                        [GoogleAIHostedTools.UrlContext] = urlContext,
                    },
                },
            },
        };

    private static RedStarOptions WithGoogleAI(string thinkingEffort = "", bool includeThoughts = true) =>
        new()
        {
            Agents = new AgentsOptions
            {
                GoogleAI = new GoogleAIAgentOptions
                {
                    ApiKey = "test-key",
                    ThinkingEffort = thinkingEffort,
                    IncludeThoughts = includeThoughts,
                },
            },
        };
}
