using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RedStar.Base.Telemetry;

namespace RedStar.Base.Agents.GoogleAI;

/// <summary>
/// Builds an <see cref="AIAgent"/> backed by Gemini via the native <c>Google.GenAI</c> SDK
/// (<see cref="Client.AsIChatClient"/>), not an OpenAI-compatible shim. Microsoft Agent Framework's
/// OpenAI-shaped abstractions don't cleanly cover Gemini/Gemma-family quirks -- most notably
/// "thinking mode" reasoning output getting lost or not coming back as a distinct block -- because an
/// OpenAI-compatible endpoint can't express Gemini-native concepts like <c>ThinkingConfig</c>. The
/// <c>Google.GenAI</c> SDK's own <c>IChatClient</c> implementation maps <see cref="ChatOptions.Reasoning"/>
/// directly onto Gemini's <c>ThinkingConfig</c> and emits thought text as a distinct
/// <c>TextReasoningContent</c> (vs. plain <c>TextContent</c> for the answer) -- picked up generically by
/// <c>RedStar.Cli.ChatEngine</c>'s existing <c>TextReasoningContent</c> handling with no extra wiring here.
/// </summary>
public static class GoogleAIAgentFactory
{
    /// <summary>
    /// Builds an <see cref="AIAgent"/> backed by Gemini. <paramref name="httpClientFactory"/> is plugged
    /// directly into <see cref="ClientOptions.HttpClientFactory"/> (typically wrapping a named
    /// <see cref="IHttpClientFactory"/> client, same pipeline as the other HTTP-based agents; unlike
    /// Unsloth/LMStudio, no <see cref="ConditionalAuthHandler"/> is needed since Gemini always requires a
    /// real API key and the SDK sets its own <c>x-goog-api-key</c> header) -- it must stay a factory rather
    /// than an already-built <see cref="HttpClient"/> so the <c>Google.GenAI</c> SDK's <c>ApiClient</c>
    /// controls when the client actually gets constructed (lazily, on first use, then cached), instead of
    /// this method forcing eager construction on every call regardless of whether the SDK ever needs it.
    /// <paramref name="instructions"/> becomes the agent's system prompt (merged into
    /// <see cref="ChatOptions.Instructions"/> on every run by <see cref="ChatClientAgent"/>) rather than a
    /// message the caller has to manage.
    /// </summary>
    /// <param name="tools">
    /// Client-side tools (typically <see cref="AIFunctionFactory"/>-created <see cref="AIFunction"/>s, or
    /// any other <see cref="AITool"/>) to make available to the model, e.g. via
    /// <see cref="Microsoft.Extensions.AI.ChatOptions.Tools"/>. This is purely an injection point -- no
    /// concrete tool is passed by any caller today; a future tool registry plugs in here. When non-empty,
    /// the underlying <c>IChatClient</c> is wrapped with <c>UseFunctionInvocation()</c> so
    /// <see cref="FunctionCallContent"/>/<see cref="FunctionResultContent"/> round-trips (including
    /// multi-turn tool-calling) are driven automatically by the framework rather than by hand-rolled
    /// dispatch code here. Thought-signature handling across a tool-calling turn needs no special
    /// handling in RedStar: the <c>Google.GenAI</c> SDK's own message-to-request conversion already looks
    /// for the <see cref="TextReasoningContent"/> immediately preceding a <see cref="FunctionCallContent"/>
    /// in history and reuses its signature verbatim, or substitutes its own "skip validation" placeholder
    /// when none is present (e.g. because <see cref="GoogleAIAgentOptions.IncludeThoughts"/> is
    /// <c>false</c>) -- so a thought trace is transparently kept when Gemini needs it to validate a
    /// function call and never has to be stripped by hand for ordinary turns. This only works because
    /// <c>RedStar.Base.ChatSession</c> preserves every <see cref="AIContent"/> the model returns
    /// (including <see cref="TextReasoningContent"/>) verbatim in history -- see its remarks.
    /// </param>
    public static AIAgent Create(
        Func<HttpClient> httpClientFactory, RedStarOptions options, string modelId, string? instructions = null,
        IEnumerable<AITool>? tools = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(modelId);

        RedStarTelemetry.CreateLogger("RedStar.Base.Agents.GoogleAI.GoogleAIAgentFactory")
            .LogBuildingAgent(modelId);

        var googleAI = options.Agents.GoogleAI;

        if (string.IsNullOrEmpty(googleAI.ApiKey))
        {
            throw new InvalidOperationException(
                "Google AI API key is required. Set it via --api-key, the " +
                "RedStar__Agents__GoogleAI__ApiKey environment variable, or appsettings.local.json.");
        }

        var client = new Client(
            apiKey: googleAI.ApiKey,
            httpOptions: new HttpOptions { BaseUrl = googleAI.BaseUrl },
            clientOptions: new ClientOptions { HttpClientFactory = httpClientFactory });

        var toolList = tools as IReadOnlyList<AITool> ?? tools?.ToList();
        var chatOptions = CreateChatOptions(options, toolList);
        chatOptions.Instructions = instructions;

        IChatClient chatClient = client.AsIChatClient(modelId);
        if (toolList is { Count: > 0 })
        {
            chatClient = chatClient.AsBuilder().UseFunctionInvocation().Build();
        }

        return chatClient.AsAIAgent(new ChatClientAgentOptions { ChatOptions = chatOptions });
    }

    /// <summary>
    /// Builds the <see cref="ChatOptions"/> to pass alongside each request. Maps
    /// <see cref="GoogleAIAgentOptions.ThinkingEffort"/>/<see cref="GoogleAIAgentOptions.IncludeThoughts"/>
    /// onto <see cref="ChatOptions.Reasoning"/>, which the <c>Google.GenAI</c> SDK's <c>IChatClient</c>
    /// translates into Gemini's <c>ThinkingConfig</c> (<c>ThinkingBudget</c>/<c>ThinkingLevel</c> from
    /// <c>Effort</c>, <c>IncludeThoughts</c> from <c>Output</c>). Left as <c>null</c> only when
    /// <see cref="GoogleAIAgentOptions.ThinkingEffort"/> is blank/unrecognized and
    /// <see cref="GoogleAIAgentOptions.IncludeThoughts"/> is <c>false</c> -- i.e. only when there is
    /// nothing to configure -- so the model's own default thinking behavior applies untouched. Also
    /// carries every inference-parameter field on <see cref="GoogleAIAgentOptions"/>
    /// (<c>Temperature</c>/<c>TopP</c>/<c>TopK</c>/<c>MaxOutputTokens</c>/<c>FrequencyPenalty</c>/
    /// <c>PresencePenalty</c>/<c>Seed</c>/<c>StopSequences</c>) straight onto the matching
    /// <see cref="ChatOptions"/> property -- these are all natively modeled by
    /// <c>Microsoft.Extensions.AI</c> and mapped into Gemini's <c>GenerateContentConfig</c> by the SDK's
    /// <c>IChatClient</c> itself, so unlike Unsloth's <c>enable_tools</c>/<c>enabled_tools</c> there's no
    /// provider-specific <c>Patch</c>/<c>RawRepresentationFactory</c> step needed here. <paramref name="tools"/>
    /// (see <see cref="Create"/>'s remarks) and every enabled entry of
    /// <see cref="GoogleAIAgentOptions.HostedTools"/> found in <see cref="GoogleAIHostedTools.MappedTools"/>
    /// are merged onto <see cref="ChatOptions.Tools"/>; left <c>null</c> when neither contributes anything,
    /// so an empty tool list never gets sent as an empty array. Every enabled entry found in
    /// <see cref="GoogleAIHostedTools.NativeOnlyTools"/> instead accumulates into a single list handed to
    /// <see cref="ChatOptions.RawRepresentationFactory"/> as a <c>GenerateContentConfig</c> -- the SDK
    /// starts request construction from whatever that factory returns and then *appends* the
    /// <see cref="ChatOptions.Tools"/>-derived entries to its (already non-null) <c>Tools</c> list, so the
    /// two mechanisms compose safely rather than one clobbering the other. An unrecognized
    /// <see cref="GoogleAIAgentOptions.HostedTools"/> key matches neither table and is silently ignored,
    /// same precedent as elsewhere in this codebase. Enabled keys are also deduplicated case-insensitively
    /// as the dictionary is walked, so a hand-built <see cref="GoogleAIAgentOptions.HostedTools"/> using an
    /// ordinal comparer with both <c>"GoogleSearch"</c> and <c>"googlesearch"</c> set <c>true</c> still adds
    /// the tool only once -- normal config binding can't produce this (it merges into one
    /// case-insensitive key, see <see cref="GoogleAIAgentOptions.HostedTools"/>'s remarks), but a caller
    /// bypassing that default shouldn't be able to double-add a hosted tool either. <b>This is the only
    /// place in the GoogleAI agent that
    /// sets <see cref="ChatOptions.RawRepresentationFactory"/></b> -- any future raw-config need (e.g.
    /// safety settings) must extend the same accumulated list this method builds rather than overwrite the
    /// factory outright, or it will silently drop whichever native-only hosted tools were also requested.
    /// </summary>
    /// <summary>
    /// Every <see cref="ReasoningEffort"/> member's name, used to validate
    /// <see cref="GoogleAIAgentOptions.ThinkingEffort"/> by name rather than via <see cref="Enum.TryParse"/>
    /// directly -- <c>TryParse</c> also accepts numeric strings (e.g. <c>"3"</c>) that don't name a real
    /// member, which this method must reject the same as any other unrecognized value.
    /// </summary>
    private static readonly HashSet<string> KnownReasoningEffortNames =
        new(Enum.GetNames<ReasoningEffort>(), StringComparer.OrdinalIgnoreCase);

    public static ChatOptions CreateChatOptions(RedStarOptions options, IReadOnlyList<AITool>? tools = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var googleAI = options.Agents.GoogleAI;
        var chatOptions = new ChatOptions
        {
            Temperature = (float?)googleAI.Temperature,
            TopP = (float?)googleAI.TopP,
            TopK = googleAI.TopK,
            MaxOutputTokens = googleAI.MaxOutputTokens,
            FrequencyPenalty = (float?)googleAI.FrequencyPenalty,
            PresencePenalty = (float?)googleAI.PresencePenalty,
            Seed = googleAI.Seed,
        };

        if (googleAI.StopSequences.Count > 0)
        {
            chatOptions.StopSequences = googleAI.StopSequences;
        }

        List<AITool>? allTools = null;
        List<Tool>? nativeOnlyTools = null;
        var addedHostedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, enabled) in googleAI.HostedTools)
        {
            if (!enabled || !addedHostedTools.Add(name))
            {
                continue;
            }

            if (GoogleAIHostedTools.MappedTools.TryGetValue(name, out var toMappedTool))
            {
                (allTools ??= []).Add(toMappedTool());
            }
            else if (GoogleAIHostedTools.NativeOnlyTools.TryGetValue(name, out var toNativeTool))
            {
                (nativeOnlyTools ??= []).Add(toNativeTool());
            }
        }

        if (nativeOnlyTools is { Count: > 0 })
        {
            chatOptions.RawRepresentationFactory = _ => new GenerateContentConfig { Tools = new List<Tool>(nativeOnlyTools) };
        }

        if (tools is { Count: > 0 })
        {
            (allTools ??= []).AddRange(tools);
        }

        if (allTools is { Count: > 0 })
        {
            chatOptions.Tools = allTools;
        }

        ReasoningEffort? effort = null;
        var trimmedThinkingEffort = googleAI.ThinkingEffort?.Trim();
        if (!string.IsNullOrEmpty(trimmedThinkingEffort) && KnownReasoningEffortNames.Contains(trimmedThinkingEffort))
        {
            effort = Enum.Parse<ReasoningEffort>(trimmedThinkingEffort, ignoreCase: true);
        }

        if (effort is not null || googleAI.IncludeThoughts)
        {
            chatOptions.Reasoning = new ReasoningOptions
            {
                Effort = effort,
                Output = googleAI.IncludeThoughts ? ReasoningOutput.Summary : ReasoningOutput.None,
            };
        }

        return chatOptions;
    }
}
