namespace RedStar.Base;

public sealed class RedStarOptions
{
    public const string SectionName = "RedStar";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8888/v1";
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Model used when a command doesn't specify one explicitly. Left empty, the server's
    /// currently loaded model is auto-detected instead (see <see cref="ModelSelector"/>).
    /// </summary>
    public string DefaultModel { get; set; } = "";

    /// <summary>
    /// When true, requests opt into Unsloth's server-side web search tool
    /// (sent as <c>enabled_tools: ["web_search"]</c>). See <see cref="RedStarChatClientFactory.CreateChatOptions"/>.
    /// </summary>
    public bool WebSearchEnabled { get; set; }

    /// <summary>
    /// Returns a copy with any non-blank overrides applied, leaving unspecified ones untouched.
    /// Clones via <see cref="MemberwiseClone"/> rather than a field-by-field object initializer so that
    /// properties with no CLI override (like <see cref="WebSearchEnabled"/>) are carried over automatically
    /// instead of silently resetting to their default whenever a new property is added to this class.
    /// </summary>
    public RedStarOptions ApplyOverrides(string? baseUrl = null, string? apiKey = null, string? defaultModel = null)
    {
        var clone = (RedStarOptions)MemberwiseClone();
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            clone.BaseUrl = baseUrl;
        }

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            clone.ApiKey = apiKey;
        }

        if (!string.IsNullOrWhiteSpace(defaultModel))
        {
            clone.DefaultModel = defaultModel;
        }

        return clone;
    }
}
