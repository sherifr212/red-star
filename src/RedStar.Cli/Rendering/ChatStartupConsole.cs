using Microsoft.Extensions.Logging;
using RedStar.Base;
using RedStar.Base.Agents.ClaudeCode;
using RedStar.Cli.Infrastructure;
using Spectre.Console;
using System.Diagnostics;
using System.Collections.Generic;
using System;
using System.Linq;

namespace RedStar.Cli.Rendering;

internal static class ChatStartupConsole
{
    /// <summary>
    /// Prints a boxed summary of this run's effective configuration -- which agent, connection details
    /// (endpoint+API key for Unsloth/LM Studio; CLI path/auth mode/process mode for ClaudeCode),
    /// the resolved model plus how it was picked, every known tool's on/off state,
    /// and telemetry export -- once per run, before any chat request goes out.
    /// Mirrors the same fields onto <paramref name="activity"/>'s tags (<c>redstar.config.*</c>)
    /// and one structured log line.
    /// </summary>
    public static void PrintStartupInfoBox(
        RedStarOptions options, ActiveAgentSettings active, string runId, string modelId, string modelSourceLabel,
        Activity? activity, ILogger logger)
    {
        var isClaudeCode = active.AgentName == AgentNames.ClaudeCode;
        var apiKeyConfigured = !string.IsNullOrEmpty(active.ApiKey);

        var table = new Table().Border(TableBorder.None).HideHeaders();
        table.AddColumn(new TableColumn(string.Empty).NoWrap());
        table.AddColumn(string.Empty);
        table.AddRow("[grey]Agent[/]", Markup.Escape(active.AgentName));
        table.AddRow("[grey]Run ID[/]", Markup.Escape(runId));

        if (isClaudeCode)
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
        else
        {
            table.AddRow("[grey]Endpoint[/]", Markup.Escape(active.BaseUrl));
            table.AddRow("[grey]API key[/]", apiKeyConfigured ? "[green]configured[/]" : "[yellow]not configured[/]");
        }

        table.AddRow("[grey]Model[/]", $"[green]{Markup.Escape(modelId)}[/] [grey]({modelSourceLabel})[/]");
        if (active.Tools is { } tools)
        {
            table.AddRow("[grey]Tools[/]", FormatToolsSummary(tools, active.KnownToolNames ?? []));
        }

        var otel = options.Otel;
        table.AddRow(
            "[grey]Telemetry[/]",
            otel.Enabled ? $"[green]enabled[/] -> {Markup.Escape(otel.Endpoint)}" : "disabled");

        var panel = new Panel(table)
            .Header("[bold]Startup configuration[/]")
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .Expand();
        AnsiConsole.Write(panel);

        activity?.SetTag("redstar.config.agent", active.AgentName);
        if (isClaudeCode)
        {
            var claudeCode = options.Agents.ClaudeCode;
            activity?.SetTag("redstar.config.claude_code.cli_path", claudeCode.CliPath);
            activity?.SetTag("redstar.config.claude_code.auth_mode", claudeCode.AuthMode);
            activity?.SetTag("redstar.config.claude_code.process_mode", claudeCode.ProcessMode);
            activity?.SetTag("redstar.config.claude_code.bare", claudeCode.Bare);
        }
        else
        {
            activity?.SetTag("redstar.config.endpoint", active.BaseUrl);
            activity?.SetTag("redstar.config.api_key_configured", apiKeyConfigured);
        }

        activity?.SetTag("redstar.config.model", modelId);
        activity?.SetTag("redstar.config.model_source", modelSourceLabel);
        if (active.Tools is { } toolsForTag)
        {
            activity?.SetTag("redstar.config.enabled_tools", string.Join(",", toolsForTag));
        }

        activity?.SetTag("redstar.config.telemetry_enabled", otel.Enabled);
        activity?.SetTag("redstar.config.telemetry_endpoint", otel.Endpoint);

        logger.LogInformation(
            "Startup configuration for run {RunId}: agent={Agent} endpoint={Endpoint} apiKeyConfigured={ApiKeyConfigured} " +
            "model={ModelId} modelSource={ModelSource} enabledTools={EnabledTools} " +
            "telemetryEnabled={TelemetryEnabled} telemetryEndpoint={TelemetryEndpoint}",
            runId, active.AgentName, active.BaseUrl, apiKeyConfigured, modelId, modelSourceLabel,
            active.Tools is null ? "n/a" : string.Join(",", active.Tools), otel.Enabled, otel.Endpoint);
    }

    /// <summary>
    /// Renders every tool in <paramref name="knownToolNames"/> plus any extra names present in
    /// <paramref name="enabledTools"/> that aren't in that list -- one per line, each tagged with its current
    /// enabled/disabled state.
    /// </summary>
    private static string FormatToolsSummary(IReadOnlyList<string> enabledTools, IReadOnlyList<string> knownToolNames)
    {
        var enabledSet = new HashSet<string>(enabledTools, StringComparer.OrdinalIgnoreCase);
        var names = knownToolNames
            .Concat(enabledTools.Where(t => !knownToolNames.Contains(t, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(
            "\n",
            names.Select(name => enabledSet.Contains(name)
                ? $"[green]{Markup.Escape(name)}: enabled[/]"
                : $"[grey]{Markup.Escape(name)}: disabled[/]"));
    }
}
