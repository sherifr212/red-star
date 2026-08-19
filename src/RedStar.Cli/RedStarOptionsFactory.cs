using Microsoft.Extensions.Configuration;

using RedStar.Base;

namespace RedStar.Cli;

/// <summary>
/// Builds a <see cref="RedStarOptions"/> by layering appsettings.json -> appsettings.local.json ->
/// environment variables, then applying whichever CLI flags were actually passed (see
/// <see cref="RedStarOptions.ApplyOverrides"/> for the "non-blank wins" rule).
/// </summary>
internal static class RedStarOptionsFactory
{
    public static RedStarOptions Build(
        string? agent, string? endpoint, string? apiKey, string? defaultModel = null,
        ClaudeCodeOverrides? claudeCode = null, GoogleAIOverrides? googleAI = null, string? dbConnectionString = null)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var options = new RedStarOptions();
        configuration.GetSection(RedStarOptions.SectionName).Bind(options);

        return options.ApplyOverrides(
            agent: agent, baseUrl: endpoint, apiKey: apiKey, defaultModel: defaultModel,
            claudeCode: claudeCode, googleAI: googleAI);
    }
}