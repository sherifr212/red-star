using Microsoft.Extensions.Logging;
using RedStar.Base;
using RedStar.Base.Agents.ClaudeCode;
using RedStar.Cli.Infrastructure;
using Spectre.Console;
using System;
using System.Diagnostics;
using System.Text.Json;

namespace RedStar.Cli.Rendering;

/// <summary>
/// Strategy for rendering one agent's startup-config details -- <see cref="ChatStartupConsole"/> picks the
/// implementation matching <see cref="ActiveAgentSettings.AgentName"/> and delegates every agent-specific
/// piece of the startup box/activity tags/log line to it, so a new agent's quirks (ClaudeCode's process
/// spawning knobs, GoogleAI's sampling parameters, ...) don't grow another branch in that shared class.
/// </summary>
internal interface IAgentStartupInfoRenderer
{
    /// <summary>Adds this agent's connection rows (e.g. endpoint/API key, or ClaudeCode's CLI path/auth mode).</summary>
    void AddConnectionRows(Table table, RedStarOptions options, ActiveAgentSettings active, bool apiKeyConfigured);

    /// <summary>Adds this agent's connection fields as activity tags, mirroring <see cref="AddConnectionRows"/>.</summary>
    void AddConnectionTags(Activity? activity, RedStarOptions options, ActiveAgentSettings active, bool apiKeyConfigured);

    /// <summary>Adds any extra rows beyond the common agent/model/tools/telemetry ones (e.g. GoogleAI's sampling config).</summary>
    void AddExtraRows(Table table, RedStarOptions options, ActiveAgentSettings active);

    /// <summary>Adds any extra activity tags mirroring <see cref="AddExtraRows"/>.</summary>
    void AddExtraTags(Activity? activity, RedStarOptions options, ActiveAgentSettings active);

    /// <summary>Emits any extra structured log line beyond the common one (e.g. GoogleAI's full config as JSON).</summary>
    void LogExtra(ILogger logger, string runId, RedStarOptions options, ActiveAgentSettings active);
}

/// <summary>Unsloth/LM Studio: a plain OpenAI-compatible endpoint, no extra rows/tags/logging.</summary>
internal sealed class DefaultAgentStartupInfoRenderer : IAgentStartupInfoRenderer
{
    public void AddConnectionRows(Table table, RedStarOptions options, ActiveAgentSettings active, bool apiKeyConfigured)
    {
        table.AddRow("[grey]Endpoint[/]", Markup.Escape(active.BaseUrl));
        table.AddRow("[grey]API key[/]", apiKeyConfigured ? "[green]configured[/]" : "[yellow]not configured[/]");
    }

    public void AddConnectionTags(Activity? activity, RedStarOptions options, ActiveAgentSettings active, bool apiKeyConfigured)
    {
        activity?.SetTag("redstar.config.endpoint", active.BaseUrl);
        activity?.SetTag("redstar.config.api_key_configured", apiKeyConfigured);
    }

    public void AddExtraRows(Table table, RedStarOptions options, ActiveAgentSettings active)
    {
    }

    public void AddExtraTags(Activity? activity, RedStarOptions options, ActiveAgentSettings active)
    {
    }

    public void LogExtra(ILogger logger, string runId, RedStarOptions options, ActiveAgentSettings active)
    {
    }
}

/// <summary>ClaudeCode: no HTTP endpoint at all -- shows CLI path/auth mode/process mode instead.</summary>
internal sealed class ClaudeCodeStartupInfoRenderer : IAgentStartupInfoRenderer
{
    public void AddConnectionRows(Table table, RedStarOptions options, ActiveAgentSettings active, bool apiKeyConfigured)
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
    }

    public void AddConnectionTags(Activity? activity, RedStarOptions options, ActiveAgentSettings active, bool apiKeyConfigured)
    {
        var claudeCode = options.Agents.ClaudeCode;
        activity?.SetTag("redstar.config.claude_code.cli_path", claudeCode.CliPath);
        activity?.SetTag("redstar.config.claude_code.auth_mode", claudeCode.AuthMode);
        activity?.SetTag("redstar.config.claude_code.process_mode", claudeCode.ProcessMode);
        activity?.SetTag("redstar.config.claude_code.bare", claudeCode.Bare);
    }

    public void AddExtraRows(Table table, RedStarOptions options, ActiveAgentSettings active)
    {
    }

    public void AddExtraTags(Activity? activity, RedStarOptions options, ActiveAgentSettings active)
    {
    }

    public void LogExtra(ILogger logger, string runId, RedStarOptions options, ActiveAgentSettings active)
    {
    }
}

/// <summary>
/// GoogleAI: same endpoint/API-key connection rows as the default renderer, plus every one of
/// <see cref="GoogleAIAgentOptions"/>'s tunables -- shown individually in the console table (thinking
/// mode, every sampling knob), and logged/tagged as one JSON blob rather than field-by-field, since the
/// set of tunables is large and keeps growing (Temperature/TopP/TopK/... today) -- adding a new one only
/// needs a table row here, never a new tag/log field to remember. The JSON blob omits <c>ApiKey</c>
/// (already covered by the <c>apiKeyConfigured</c> connection tag/row) and <c>BaseUrl</c> (already its
/// own connection tag/row).
/// </summary>
internal sealed class GoogleAIStartupInfoRenderer : IAgentStartupInfoRenderer
{
    public void AddConnectionRows(Table table, RedStarOptions options, ActiveAgentSettings active, bool apiKeyConfigured)
    {
        table.AddRow("[grey]Endpoint[/]", Markup.Escape(active.BaseUrl));
        table.AddRow("[grey]API key[/]", apiKeyConfigured ? "[green]configured[/]" : "[yellow]not configured[/]");
    }

    public void AddConnectionTags(Activity? activity, RedStarOptions options, ActiveAgentSettings active, bool apiKeyConfigured)
    {
        activity?.SetTag("redstar.config.endpoint", active.BaseUrl);
        activity?.SetTag("redstar.config.api_key_configured", apiKeyConfigured);
    }

    public void AddExtraRows(Table table, RedStarOptions options, ActiveAgentSettings active)
    {
        var googleAI = options.Agents.GoogleAI;
        var thinkingEffortLabel = string.IsNullOrWhiteSpace(googleAI.ThinkingEffort) ? "model default" : googleAI.ThinkingEffort;

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
    }

    public void AddExtraTags(Activity? activity, RedStarOptions options, ActiveAgentSettings active)
    {
        activity?.SetTag("redstar.config.google_ai.config_json", BuildConfigJson(options));
    }

    public void LogExtra(ILogger logger, string runId, RedStarOptions options, ActiveAgentSettings active)
    {
        logger.LogInformation(
            "GoogleAI configuration for run {RunId}: {GoogleAiConfigJson}", runId, BuildConfigJson(options));
    }

    private static string BuildConfigJson(RedStarOptions options)
    {
        var googleAI = options.Agents.GoogleAI;
        return JsonSerializer.Serialize(new
        {
            googleAI.ThinkingEffort,
            googleAI.IncludeThoughts,
            googleAI.Temperature,
            googleAI.TopP,
            googleAI.TopK,
            googleAI.MaxOutputTokens,
            googleAI.FrequencyPenalty,
            googleAI.PresencePenalty,
            googleAI.Seed,
            googleAI.StopSequences,
            googleAI.HostedTools,
        });
    }
}

/// <summary>Picks the <see cref="IAgentStartupInfoRenderer"/> matching an <see cref="ActiveAgentSettings.AgentName"/>.</summary>
internal static class AgentStartupInfoRendererFactory
{
    public static IAgentStartupInfoRenderer Create(string agentName)
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
