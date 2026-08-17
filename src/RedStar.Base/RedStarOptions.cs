namespace RedStar.Base;

public sealed class RedStarOptions
{
    public const string SectionName = "RedStar";

    /// <summary>
    /// Which agent backend this run talks to -- one of <see cref="AgentNames"/>. Selects both the default
    /// <c>agentFactory</c>/<c>responseExtractor</c>/<c>modelsClientFactory</c> in
    /// <c>RedStar.Cli.ChatCommandHandler.RunAsync</c> (an explicit two-way switch there, not a registry --
    /// see the remarks on <see cref="AgentNames"/>) and which nested <see cref="Agents"/> section
    /// <see cref="ApplyOverrides"/> applies <c>baseUrl</c>/<c>apiKey</c>/<c>defaultModel</c> overrides to.
    /// Matched case-insensitively; an unrecognized value is treated the same as <see cref="AgentNames.Unsloth"/>
    /// rather than erroring, matching the CLI's generally permissive flag handling elsewhere.
    /// </summary>
    public string Agent { get; set; } = AgentNames.Unsloth;

    /// <summary>
    /// Per-agent settings, nested so agent-specific config (e.g. Unsloth's or LM Studio's connection/behavior
    /// settings) never reads as a global RedStar setting. See <see cref="AgentsOptions"/>.
    /// </summary>
    public AgentsOptions Agents { get; set; } = new();

    /// <summary>
    /// OpenTelemetry export settings (traces/logs/metrics to an OTLP collector, e.g. the standalone
    /// Aspire Dashboard). Config/env-only -- no CLI override. Stays top-level since it's genuinely
    /// agent-agnostic, not specific to any one agent under <see cref="Agents"/>.
    /// </summary>
    public OtelOptions Otel { get; set; } = new();

    /// <summary>
    /// Returns a copy with any non-blank overrides applied. <paramref name="agent"/> (if non-blank) is applied
    /// first and determines which single nested agent section under <see cref="Agents"/> the remaining
    /// <paramref name="baseUrl"/>/<paramref name="apiKey"/>/<paramref name="defaultModel"/> overrides land on --
    /// <see cref="AgentNames.LMStudio"/> (case-insensitive) routes to <see cref="AgentsOptions.LMStudio"/>,
    /// <see cref="AgentNames.GoogleAI"/> routes to <see cref="AgentsOptions.GoogleAI"/>,
    /// anything else (including no override, meaning whatever <see cref="Agent"/> already was) routes to
    /// <see cref="AgentsOptions.Unsloth"/>. The other agent's section is left completely untouched either way.
    /// Clones via <see cref="MemberwiseClone"/> rather than a field-by-field object initializer so that
    /// properties with no CLI override (like <see cref="OtelOptions"/> or
    /// <see cref="UnslothAgentOptions.EnabledTools"/>) are carried over automatically instead of
    /// silently resetting to their default whenever a new property is added to this class or its nested
    /// option types. Only reassigns <see cref="Agents"/>/the targeted nested options record when at least one
    /// of <paramref name="baseUrl"/>/<paramref name="apiKey"/>/<paramref name="defaultModel"/> is non-blank, so
    /// the clone stays aliased to the original's (record) instances -- same as <see cref="MemberwiseClone"/>
    /// already does for <see cref="Otel"/> -- rather than needing a deep clone up front.
    /// </summary>
    public RedStarOptions ApplyOverrides(
        string? agent = null, string? baseUrl = null, string? apiKey = null, string? defaultModel = null)
    {
        var clone = (RedStarOptions)MemberwiseClone();

        if (!string.IsNullOrWhiteSpace(agent))
        {
            clone.Agent = agent;
        }

        if (string.IsNullOrWhiteSpace(baseUrl) && string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(defaultModel))
        {
            return clone;
        }

        if (string.Equals(clone.Agent, AgentNames.GoogleAI, StringComparison.OrdinalIgnoreCase))
        {
            var googleAI = clone.Agents.GoogleAI;
            var overriddenGoogleAI = googleAI with
            {
                BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? googleAI.BaseUrl : baseUrl,
                ApiKey = string.IsNullOrWhiteSpace(apiKey) ? googleAI.ApiKey : apiKey,
                DefaultModel = string.IsNullOrWhiteSpace(defaultModel) ? googleAI.DefaultModel : defaultModel,
            };
            clone.Agents = clone.Agents with { GoogleAI = overriddenGoogleAI };
            return clone;
        }

        if (string.Equals(clone.Agent, AgentNames.LMStudio, StringComparison.OrdinalIgnoreCase))
        {
            var lmStudio = clone.Agents.LMStudio;
            var overriddenLMStudio = lmStudio with
            {
                BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? lmStudio.BaseUrl : baseUrl,
                ApiKey = string.IsNullOrWhiteSpace(apiKey) ? lmStudio.ApiKey : apiKey,
                DefaultModel = string.IsNullOrWhiteSpace(defaultModel) ? lmStudio.DefaultModel : defaultModel,
            };
            clone.Agents = clone.Agents with { LMStudio = overriddenLMStudio };
            return clone;
        }

        var unsloth = clone.Agents.Unsloth;
        var overriddenUnsloth = unsloth with
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? unsloth.BaseUrl : baseUrl,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? unsloth.ApiKey : apiKey,
            DefaultModel = string.IsNullOrWhiteSpace(defaultModel) ? unsloth.DefaultModel : defaultModel,
        };
        clone.Agents = clone.Agents with { Unsloth = overriddenUnsloth };

        return clone;
    }
}

/// <summary>Per-agent config sections. See <see cref="RedStarOptions.Agents"/>.</summary>
public sealed record AgentsOptions
{
    public UnslothAgentOptions Unsloth { get; set; } = new();
    public LMStudioAgentOptions LMStudio { get; set; } = new();
    public GoogleAIAgentOptions GoogleAI { get; set; } = new();
}

/// <summary>Unsloth agent connection/behavior settings, nested at <c>RedStar:Agents:Unsloth:*</c>.</summary>
public sealed record UnslothAgentOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8888/v1";
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Model used when a command doesn't specify one explicitly. Left empty, the server's
    /// currently loaded model is auto-detected instead (see <see cref="ModelSelector"/>).
    /// </summary>
    public string DefaultModel { get; set; } = "";

    /// <summary>
    /// Names of Unsloth server-side tools to opt into, sent verbatim as <c>enabled_tools</c> (e.g.
    /// <c>["web_search", "python"]</c>); <c>enable_tools</c> is only sent when this is non-empty. Free-form
    /// -- any name the server recognizes works via config alone, no code change required -- see
    /// <see cref="RedStar.Base.Agents.Unsloth.UnslothTools"/> for the documented names and
    /// <see cref="RedStar.Base.Agents.Unsloth.UnslothAgentFactory.CreateChatOptions"/> for how this is sent.
    /// Config/env-only, no CLI flag.
    /// </summary>
    public List<string> EnabledTools { get; set; } = [];
}

/// <summary>
/// LM Studio agent connection/behavior settings, nested at <c>RedStar:Agents:LMStudio:*</c>. No
/// <c>EnabledTools</c> equivalent -- LM Studio has no built-in server-side tools, unlike Unsloth.
/// Default <see cref="BaseUrl"/> matches LM Studio's default local server port (1234); LM Studio's
/// native REST endpoints (used by <see cref="RedStar.Base.Agents.LMStudio.LMStudioModelsClient"/> for
/// richer model listing) hang off the same host/port, under <c>/api/v0/*</c> instead of <c>/v1/*</c>.
/// </summary>
public sealed record LMStudioAgentOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:1234/v1";

    /// <summary>Empty by default -- LM Studio's server has authentication disabled out of the box, unlike
    /// Unsloth. Only needed if the user has explicitly enabled an API token in LM Studio's Server Settings.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Model used when a command doesn't specify one explicitly. Left empty, the server's currently loaded
    /// model is auto-detected the same way as Unsloth's (see <see cref="ModelSelector"/>) -- but unlike
    /// Unsloth, a configured value here that's known to the server but not currently loaded doesn't have to
    /// be a hard failure: LM Studio can load it on demand (see <see cref="ModelSelector.SelectDefault"/>'s
    /// <c>allowJitLoad</c> parameter).
    /// </summary>
    public string DefaultModel { get; set; } = "";
}

/// <summary>
/// Google AI agent connection/behavior settings, nested at <c>RedStar:Agents:GoogleAI:*</c>.
/// Google AI Studio provides an OpenAI-compatible API endpoint for chat completions and model listing.
/// Default model is Gemma 4 31B which is available on Google AI Studio.
/// </summary>
public sealed record GoogleAIAgentOptions
{
    /// <summary>
    /// Base URL for Google AI's OpenAI-compatible API endpoint. The default points to the official
    /// Google AI Studio API. This can be customized if using a compatible endpoint.
    /// </summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/openai/";

    /// <summary>
    /// API key for Google AI Studio. Required to use the Google AI agent.
    /// Generate from https://aistudio.google.com/app/apikey
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Model used when a command doesn't specify one explicitly. Defaults to "gemma-4-31b-001"
    /// (Google's Gemma 4 31B model). Other available models can be listed with the `models` command
    /// when GoogleAI agent is active.
    /// </summary>
    public string DefaultModel { get; set; } = "gemma-4-31b-001";
}

/// <summary>OpenTelemetry OTLP export settings. See <see cref="RedStarOptions.Otel"/>.</summary>
public sealed class OtelOptions
{
    /// <summary>On by default -- points at a local OTLP collector (e.g. the Aspire Dashboard) unless disabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>OTLP gRPC endpoint. Default matches the Aspire Dashboard's default OTLP intake port.</summary>
    public string Endpoint { get; set; } = "http://localhost:4317";
}
