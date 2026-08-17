using RedStar.Base.Agents.ClaudeCode;

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
    /// overrides land on -- <see cref="AgentNames.LMStudio"/> (case-insensitive) routes to
    /// <see cref="AgentsOptions.LMStudio"/>, <see cref="AgentNames.GoogleAI"/> routes to
    /// <see cref="AgentsOptions.GoogleAI"/>, <see cref="AgentNames.ClaudeCode"/> routes to
    /// <see cref="AgentsOptions.ClaudeCode"/> (<paramref name="baseUrl"/> is meaningless there -- ClaudeCode is
    /// a subprocess agent, not an HTTP one -- and is silently ignored; <paramref name="claudeCode"/> carries
    /// its own extra overrides instead), anything else (including no override, meaning whatever
    /// <see cref="Agent"/> already was) routes to <see cref="AgentsOptions.Unsloth"/>. Every other agent's
    /// section is left completely untouched. Clones via <see cref="MemberwiseClone"/> rather than a
    /// field-by-field object initializer so that properties with no CLI override (like <see cref="OtelOptions"/>
    /// or <see cref="UnslothAgentOptions.EnabledTools"/>) are carried over automatically instead of silently
    /// resetting to their default whenever a new property is added to this class or its nested option types.
    /// Only reassigns <see cref="Agents"/>/the targeted nested options record when at least one override is
    /// actually non-blank/non-null, so the clone stays aliased to the original's (record) instances -- same as
    /// <see cref="MemberwiseClone"/> already does for <see cref="Otel"/> -- rather than needing a deep clone up
    /// front.
    /// </summary>
    public RedStarOptions ApplyOverrides(
        string? agent = null, string? baseUrl = null, string? apiKey = null, string? defaultModel = null,
        ClaudeCodeOverrides? claudeCode = null)
    {
        var clone = (RedStarOptions)MemberwiseClone();

        if (!string.IsNullOrWhiteSpace(agent))
        {
            clone.Agent = agent;
        }

        var hasCommonOverride =
            !string.IsNullOrWhiteSpace(baseUrl) || !string.IsNullOrWhiteSpace(apiKey) || !string.IsNullOrWhiteSpace(defaultModel);
        var hasClaudeCodeOverride = claudeCode is not null && claudeCode.HasAny;

        if (!hasCommonOverride && !hasClaudeCodeOverride)
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

        if (string.Equals(clone.Agent, AgentNames.ClaudeCode, StringComparison.OrdinalIgnoreCase))
        {
            var claudeCodeOptions = clone.Agents.ClaudeCode;
            var overriddenClaudeCode = claudeCodeOptions with
            {
                ApiKey = string.IsNullOrWhiteSpace(apiKey) ? claudeCodeOptions.ApiKey : apiKey,
                DefaultModel = string.IsNullOrWhiteSpace(defaultModel) ? claudeCodeOptions.DefaultModel : defaultModel,
                CliPath = claudeCode?.CliPath is { Length: > 0 } cliPath ? cliPath : claudeCodeOptions.CliPath,
                AuthMode = claudeCode?.AuthMode is { Length: > 0 } authMode ? authMode : claudeCodeOptions.AuthMode,
                Bare = claudeCode?.Bare ?? claudeCodeOptions.Bare,
                ProcessMode = claudeCode?.ProcessMode is { Length: > 0 } processMode ? processMode : claudeCodeOptions.ProcessMode,
                WorkingDirectory = claudeCode?.WorkingDirectory is { Length: > 0 } workingDirectory
                    ? workingDirectory
                    : claudeCodeOptions.WorkingDirectory,
                AllowedTools = claudeCode?.AllowedTools is { Count: > 0 } allowedTools ? allowedTools.ToList() : claudeCodeOptions.AllowedTools,
                DisallowedTools = claudeCode?.DisallowedTools is { Count: > 0 } disallowedTools
                    ? disallowedTools.ToList()
                    : claudeCodeOptions.DisallowedTools,
                PermissionMode = claudeCode?.PermissionMode is { Length: > 0 } permissionMode
                    ? permissionMode
                    : claudeCodeOptions.PermissionMode,
                MaxBudgetUsd = claudeCode?.MaxBudgetUsd ?? claudeCodeOptions.MaxBudgetUsd,
            };
            clone.Agents = clone.Agents with { ClaudeCode = overriddenClaudeCode };
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
    public ClaudeCodeAgentOptions ClaudeCode { get; set; } = new();
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
/// Claude Code agent settings, nested at <c>RedStar:Agents:ClaudeCode:*</c>. Unlike
/// <see cref="UnslothAgentOptions"/>/<see cref="LMStudioAgentOptions"/>, this agent isn't an OpenAI-compatible
/// HTTP server -- there is no <c>BaseUrl</c> here at all. Every field instead configures how RedStar spawns
/// and drives the local <c>claude</c> CLI as a subprocess; see
/// <see cref="RedStar.Base.Agents.ClaudeCode.ClaudeCodeAgentFactory"/>.
/// </summary>
public sealed record ClaudeCodeAgentOptions
{
    /// <summary>Executable to spawn. Left as the bare command name, resolved via PATH, rather than an
    /// absolute path, so a per-machine install location never needs configuring.</summary>
    public string CliPath { get; set; } = "claude";

    /// <summary>One of <see cref="RedStar.Base.Agents.ClaudeCode.ClaudeCodeAuthModes"/>. Matched
    /// case-insensitively; an unrecognized value is treated the same as
    /// <see cref="RedStar.Base.Agents.ClaudeCode.ClaudeCodeAuthModes.CliLogin"/>.</summary>
    public string AuthMode { get; set; } = RedStar.Base.Agents.ClaudeCode.ClaudeCodeAuthModes.CliLogin;

    /// <summary>Only used when <see cref="AuthMode"/> is
    /// <see cref="RedStar.Base.Agents.ClaudeCode.ClaudeCodeAuthModes.ApiKey"/> -- set as the spawned process's
    /// <c>ANTHROPIC_API_KEY</c> environment variable, never as a CLI flag (the <c>claude</c> CLI has none for
    /// this). Ignored entirely in <see cref="RedStar.Base.Agents.ClaudeCode.ClaudeCodeAuthModes.CliLogin"/>
    /// mode.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Passes <c>--bare</c> when true, which skips hooks/plugins/CLAUDE.md auto-discovery and -- critically --
    /// skips OAuth keychain reads entirely, leaving <c>ANTHROPIC_API_KEY</c>/third-party-provider credentials
    /// as the only usable auth. Combining this with <see cref="AuthMode"/> ==
    /// <see cref="RedStar.Base.Agents.ClaudeCode.ClaudeCodeAuthModes.CliLogin"/> leaves the spawned process
    /// with no working credential at all -- <c>ChatCommandHandler</c> warns at startup for that combination
    /// rather than silently spawning a process that can only fail. Independent of <see cref="AuthMode"/>
    /// otherwise: it also affects auto-discovery, not just auth.
    /// </summary>
    public bool Bare { get; set; }

    /// <summary>
    /// Model used when a command doesn't specify one explicitly -- passed as <c>--model</c> verbatim (a short
    /// alias like <c>sonnet</c>/<c>opus</c>/<c>fable</c> or a full model id). Unlike
    /// <see cref="UnslothAgentOptions.DefaultModel"/>/<see cref="LMStudioAgentOptions.DefaultModel"/>, this is
    /// never resolved against a server's loaded-model list first -- ClaudeCode has no such concept, so
    /// <see cref="ModelSelector"/> is bypassed entirely for this agent. Left empty, no <c>--model</c> flag is
    /// passed and the CLI's own configured default is used.
    /// </summary>
    public string DefaultModel { get; set; } = "";

    /// <summary>One of <see cref="RedStar.Base.Agents.ClaudeCode.ClaudeCodeProcessModes"/>. Matched
    /// case-insensitively; an unrecognized value is treated the same as
    /// <see cref="RedStar.Base.Agents.ClaudeCode.ClaudeCodeProcessModes.PerTurn"/>.</summary>
    public string ProcessMode { get; set; } = RedStar.Base.Agents.ClaudeCode.ClaudeCodeProcessModes.PerTurn;

    /// <summary>Working directory for the spawned process. Left empty, RedStar's own current directory is
    /// inherited (the .NET default for a child process with no explicit
    /// <see cref="System.Diagnostics.ProcessStartInfo.WorkingDirectory"/>) -- deliberately not defaulted to
    /// anything else, since a tool-enabled ClaudeCode session (<see cref="AllowedTools"/>) gets real
    /// filesystem/shell access scoped to whatever this resolves to, and RedStar's own repo would be a
    /// dangerous silent default for that.</summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>
    /// Tool names to pass via <c>--allowedTools</c> (e.g. <c>["Read", "Grep", "Bash(git *)"]</c>). Empty by
    /// default -- deliberately opt-in, same philosophy as
    /// <see cref="UnslothAgentOptions.EnabledTools"/>: ClaudeCode's <c>Bash</c>/<c>Edit</c>/<c>Write</c> tools
    /// run with real filesystem/shell access on the machine running RedStar (unlike Unsloth's sandboxed
    /// server-side tools), so nothing is enabled until explicitly configured. See
    /// <see cref="RedStar.Base.Agents.ClaudeCode.ClaudeCodeTools.Known"/> for the names
    /// <c>ChatCommandHandler.PrintStartupInfoBox</c> always lists the on/off state of.
    /// </summary>
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>Tool names to pass via <c>--disallowedTools</c>, same syntax as <see cref="AllowedTools"/>.</summary>
    public List<string> DisallowedTools { get; set; } = [];

    /// <summary>Passed as <c>--permission-mode</c> when non-empty (one of the CLI's own choices:
    /// <c>acceptEdits</c>/<c>auto</c>/<c>bypassPermissions</c>/<c>manual</c>/<c>dontAsk</c>/<c>plan</c> -- there
    /// is deliberately no "default" value here to fall back to: the CLI accepts no such literal, so leaving
    /// this empty means the flag is omitted entirely rather than passed with a bogus value).</summary>
    public string PermissionMode { get; set; } = "";

    /// <summary>Passed as <c>--max-budget-usd</c> when set. Null means no flag is passed (no budget cap).</summary>
    public double? MaxBudgetUsd { get; set; }
}

/// <summary>
/// The subset of <see cref="ClaudeCodeAgentOptions"/> that's only settable via CLI flag/config -- never
/// resolved against a server's model list, so unlike <c>baseUrl</c>/<c>apiKey</c>/<c>defaultModel</c> (shared
/// with Unsloth/LMStudio in <see cref="RedStarOptions.ApplyOverrides"/>'s signature) these have no meaning for
/// any other agent. Every field null/empty/default means "no override" -- see
/// <see cref="RedStarOptions.ApplyOverrides"/>. Kept as a separate parameter object rather than growing
/// <see cref="RedStarOptions.ApplyOverrides"/>'s own parameter list by nine more positional parameters.
/// </summary>
public sealed record ClaudeCodeOverrides(
    string? CliPath = null,
    string? AuthMode = null,
    bool? Bare = null,
    string? ProcessMode = null,
    string? WorkingDirectory = null,
    IReadOnlyList<string>? AllowedTools = null,
    IReadOnlyList<string>? DisallowedTools = null,
    string? PermissionMode = null,
    double? MaxBudgetUsd = null)
{
    /// <summary>Whether at least one field here actually carries an override -- used by
    /// <see cref="RedStarOptions.ApplyOverrides"/> to decide whether the ClaudeCode section needs
    /// reassigning at all.</summary>
    public bool HasAny =>
        CliPath is { Length: > 0 } || AuthMode is { Length: > 0 } || Bare is not null ||
        ProcessMode is { Length: > 0 } || WorkingDirectory is { Length: > 0 } ||
        AllowedTools is { Count: > 0 } || DisallowedTools is { Count: > 0 } ||
        PermissionMode is { Length: > 0 } || MaxBudgetUsd is not null;
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
