using System;
using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using RedStar.Base;
using RedStar.Base.Agents.ClaudeCode;
using RedStar.Base.Telemetry;
using RedStar.Cli.Infrastructure;

using Spectre.Console;

namespace RedStar.Cli.Rendering;

/// <summary>
/// Strategy for rendering one agent's startup-config details -- <see cref="AgentStartupInfoRenderer.Render"/>
/// picks the implementation matching <see cref="ActiveAgentSettings.AgentName"/> and hands it the whole
/// startup box/activity/logger, so every agent-specific row/tag/log line (ClaudeCode's process spawning
/// knobs, GoogleAI's sampling parameters, ...) is encapsulated in one place instead of branching in
/// <see cref="ChatStartupConsole"/>. A single <see cref="Render"/> method (rather than separate
/// rows/tags/logging methods) keeps an implementation free to decide its own OTel shape -- e.g. GoogleAI
/// tagging/logging its config as one JSON blob -- without <see cref="ChatStartupConsole"/> orchestrating
/// each piece.
/// </summary>
internal interface IAgentStartupInfoRenderer
{
    /// <summary>
    /// Adds this agent's rows to <paramref name="table"/>, tags to <paramref name="activity"/>, and any
    /// extra structured log line via <paramref name="logger"/> -- everything beyond the common
    /// agent/run-id/model/tools/telemetry rows <see cref="ChatStartupConsole"/> already added.
    /// </summary>
    void Render(
        Table table, Activity? activity, ILogger logger, string runId, RedStarOptions options,
        ActiveAgentSettings active, bool apiKeyConfigured);
}

/// <summary>Unsloth/LM Studio: a plain OpenAI-compatible endpoint, nothing else to add.</summary>
internal sealed class DefaultAgentStartupInfoRenderer : IAgentStartupInfoRenderer
{
    public void Render(
        Table table, Activity? activity, ILogger logger, string runId, RedStarOptions options,
        ActiveAgentSettings active, bool apiKeyConfigured)
    {
        table.AddRow("[grey]Endpoint[/]", Markup.Escape(active.BaseUrl));
        table.AddRow("[grey]API key[/]", apiKeyConfigured ? "[green]configured[/]" : "[yellow]not configured[/]");

        activity?.SetTag("redstar.config.endpoint", active.BaseUrl);
        activity?.SetTag("redstar.config.api_key_configured", apiKeyConfigured);
    }
}

/// <summary>ClaudeCode: no HTTP endpoint at all -- shows CLI path/auth mode/process mode instead.</summary>
internal sealed class ClaudeCodeStartupInfoRenderer : IAgentStartupInfoRenderer
{
    public void Render(
        Table table, Activity? activity, ILogger logger, string runId, RedStarOptions options,
        ActiveAgentSettings active, bool apiKeyConfigured)
    {
        var claudeCode = options.Agents.ClaudeCode;
        table.AddRow("[grey]CLI path[/]", Markup.Escape(claudeCode.CliPath));
        table.AddRow("[grey]Auth mode[/]", Markup.Escape(claudeCode.AuthMode));
        if (string.Equals(claudeCode.AuthMode, ClaudeCodeAuthModes.ApiKey, StringComparison.OrdinalIgnoreCase))
        {
            var claudeApiKeyConfigured = !string.IsNullOrEmpty(claudeCode.ApiKey);
            table.AddRow("[grey]API key[/]", claudeApiKeyConfigured ? "[green]configured[/]" : "[yellow]not configured[/]");
        }

        table.AddRow("[grey]Process mode[/]", Markup.Escape(claudeCode.ProcessMode));
        if (claudeCode.Bare)
        {
            table.AddRow("[grey]Bare[/]", "[green]enabled[/]");
        }

        activity?.SetTag("redstar.config.claude_code.cli_path", claudeCode.CliPath);
        activity?.SetTag("redstar.config.claude_code.auth_mode", claudeCode.AuthMode);
        activity?.SetTag("redstar.config.claude_code.process_mode", claudeCode.ProcessMode);
        activity?.SetTag("redstar.config.claude_code.bare", claudeCode.Bare);
    }
}

/// <summary>
/// GoogleAI: same endpoint/API-key connection rows as the default renderer, plus every one of
/// <see cref="GoogleAIAgentOptions"/>'s tunables -- shown individually in the console table (thinking
/// mode, every sampling knob), and logged/tagged as one JSON blob rather than field-by-field, since the
/// set of tunables is large and keeps growing (Temperature/TopP/TopK/... today) -- adding a new one only
/// needs a table row here, never a new tag/log field to remember.
/// </summary>
internal sealed class GoogleAIStartupInfoRenderer : IAgentStartupInfoRenderer
{
    public void Render(
        Table table, Activity? activity, ILogger logger, string runId, RedStarOptions options,
        ActiveAgentSettings active, bool apiKeyConfigured)
    {
        var googleAI = options.Agents.GoogleAI;
        var thinkingEffortLabel = string.IsNullOrWhiteSpace(googleAI.ThinkingEffort) ? "model default" : googleAI.ThinkingEffort;

        table.AddRow("[grey]Endpoint[/]", Markup.Escape(active.BaseUrl));
        table.AddRow("[grey]API key[/]", apiKeyConfigured ? "[green]configured[/]" : "[yellow]not configured[/]");
        table.AddRow("[grey]Thinking effort[/]", Markup.Escape(thinkingEffortLabel));
        table.AddRow("[grey]Include thoughts[/]", googleAI.IncludeThoughts ? "[green]enabled[/]" : "[grey]disabled[/]");
        table.AddRow("[grey]Temperature[/]", Markup.Escape(googleAI.Temperature?.ToString() ?? "model default"));
        table.AddRow("[grey]Top P[/]", Markup.Escape(googleAI.TopP?.ToString() ?? "model default"));
        table.AddRow("[grey]Top K[/]", Markup.Escape(googleAI.TopK?.ToString() ?? "model default"));
        table.AddRow("[grey]Max output tokens[/]", Markup.Escape(googleAI.MaxOutputTokens?.ToString() ?? "model default"));
        table.AddRow("[grey]Frequency penalty[/]", Markup.Escape(googleAI.FrequencyPenalty?.ToString() ?? "model default"));
        table.AddRow("[grey]Presence penalty[/]", Markup.Escape(googleAI.PresencePenalty?.ToString() ?? "model default"));
        table.AddRow("[grey]Seed[/]", Markup.Escape(googleAI.Seed?.ToString() ?? "random"));
        table.AddRow(
            "[grey]Stop sequences[/]",
            googleAI.StopSequences.Count == 0 ? "none" : Markup.Escape(string.Join(", ", googleAI.StopSequences)));

        activity?.SetTag("redstar.config.endpoint", active.BaseUrl);
        activity?.SetTag("redstar.config.api_key_configured", apiKeyConfigured);

        var configJson = BuildConfigJson(googleAI);
        activity?.SetTag("redstar.config.google_ai.config_json", configJson);
        logger.LogGoogleAiConfig(runId, configJson);
    }

    /// <summary>
    /// Clones <see cref="GoogleAIAgentOptions"/> with its sensitive members nulled out (<c>ApiKey</c>
    /// today) and serializes the whole clone, rather than hand-picking individual properties -- so a
    /// future property added to <see cref="GoogleAIAgentOptions"/> shows up here automatically without
    /// this method needing to know its name.
    /// </summary>
    private static string BuildConfigJson(GoogleAIAgentOptions googleAI)
    {
        var sanitized = googleAI with { ApiKey = string.Empty };
        return JsonSerializer.Serialize(sanitized);
    }
}

/// <summary>
/// Picks the <see cref="IAgentStartupInfoRenderer"/> matching an <see cref="ActiveAgentSettings.AgentName"/>
/// and calls its <see cref="IAgentStartupInfoRenderer.Render"/> -- callers only ever need this one method,
/// never construct or dispatch on a concrete renderer themselves.
/// </summary>
internal static class AgentStartupInfoRenderer
{
    public static void Render(
        Table table, Activity? activity, ILogger logger, string runId, RedStarOptions options,
        ActiveAgentSettings active, bool apiKeyConfigured)
    {
        CreateRenderer(active.AgentName).Render(table, activity, logger, runId, options, active, apiKeyConfigured);
    }

    private static IAgentStartupInfoRenderer CreateRenderer(string agentName)
    {
        if (string.Equals(agentName, AgentNames.ClaudeCode, StringComparison.OrdinalIgnoreCase))
        {
            return new ClaudeCodeStartupInfoRenderer();
        }

        if (string.Equals(agentName, AgentNames.GoogleAI, StringComparison.OrdinalIgnoreCase))
        {
            return new GoogleAIStartupInfoRenderer();
        }

        return new DefaultAgentStartupInfoRenderer();
    }
}