namespace RedStar.Base;

public sealed class RedStarOptions
{
    public const string SectionName = "RedStar";

    /// <summary>
    /// Per-agent settings, nested so agent-specific config (e.g. Unsloth's connection/behavior
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
    /// Returns a copy with any non-blank overrides applied to the Unsloth agent's settings, leaving
    /// unspecified ones untouched. Clones via <see cref="MemberwiseClone"/> rather than a field-by-field
    /// object initializer so that properties with no CLI override (like <see cref="OtelOptions"/> or
    /// <see cref="UnslothAgentOptions.WebSearchEnabled"/>) are carried over automatically instead of
    /// silently resetting to their default whenever a new property is added to this class or its nested
    /// option types. Only reassigns <see cref="Agents"/>/its nested <see cref="UnslothAgentOptions"/> when at
    /// least one override is non-blank, so the clone stays aliased to the original's (record) instances --
    /// same as <see cref="MemberwiseClone"/> already does for <see cref="Otel"/> -- rather than needing a
    /// deep clone up front.
    /// </summary>
    public RedStarOptions ApplyOverrides(string? baseUrl = null, string? apiKey = null, string? defaultModel = null)
    {
        var clone = (RedStarOptions)MemberwiseClone();

        if (string.IsNullOrWhiteSpace(baseUrl) && string.IsNullOrWhiteSpace(apiKey) && string.IsNullOrWhiteSpace(defaultModel))
        {
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
    /// When true, requests opt into Unsloth's server-side web search tool
    /// (sent as <c>enabled_tools: ["web_search"]</c>). See
    /// <see cref="RedStar.Base.Agents.Unsloth.UnslothAgentFactory.CreateChatOptions"/>.
    /// </summary>
    public bool WebSearchEnabled { get; set; }
}

/// <summary>OpenTelemetry OTLP export settings. See <see cref="RedStarOptions.Otel"/>.</summary>
public sealed class OtelOptions
{
    /// <summary>On by default -- points at a local OTLP collector (e.g. the Aspire Dashboard) unless disabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>OTLP gRPC endpoint. Default matches the Aspire Dashboard's default OTLP intake port.</summary>
    public string Endpoint { get; set; } = "http://localhost:4317";
}
