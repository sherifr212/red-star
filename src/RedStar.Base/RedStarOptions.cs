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
    /// Returns a copy with any non-blank overrides applied, leaving unspecified ones untouched.
    /// </summary>
    public RedStarOptions ApplyOverrides(string? baseUrl = null, string? apiKey = null, string? defaultModel = null)
    {
        return new RedStarOptions
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? BaseUrl : baseUrl,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? ApiKey : apiKey,
            DefaultModel = string.IsNullOrWhiteSpace(defaultModel) ? DefaultModel : defaultModel,
        };
    }
}
