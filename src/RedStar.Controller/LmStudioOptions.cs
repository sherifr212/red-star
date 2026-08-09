namespace RedStar.Controller;

/// <summary>
/// Connection settings for the LM Studio server this gateway proxies, bound from the
/// <c>LmStudio</c> config section (<c>LmStudio:*</c>, env var <c>LmStudio__*</c>). Layered
/// appsettings.json -> appsettings.local.json -> environment variables, same precedence order as
/// RedStar.Cli's RedStarOptions (see RedStar.Cli/RedStarOptionsFactory.cs), just without a CLI-flags
/// layer since this project has no CLI surface.
/// </summary>
public sealed class LmStudioOptions
{
    public const string SectionName = "LmStudio";

    /// <summary>Base URL of the LM Studio server, e.g. "http://127.0.0.1:1234" (no trailing "/v1" -- LM Studio's native API is rooted at "/api/v1", not "/v1").</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:1234";

    /// <summary>Bearer token for LM Studio's "Require Authentication" server setting. Empty means the server has auth disabled -- no Authorization header is sent.</summary>
    public string ApiKey { get; set; } = "";
}
