using System.ComponentModel;

using Spectre.Console.Cli;

namespace RedStar.Cli.Commands;

/// <summary>Options shared by every subcommand (and the default 'chat' root behavior).</summary>
public class CommonSettings : CommandSettings
{
    [CommandOption("--agent")]
    [Description("Which agent backend to talk to: \"Unsloth\" (default) or \"LMStudio\". Falls back to RedStar__Agent env var or appsettings.")]
    public string? Agent { get; set; }

    [CommandOption("--endpoint")]
    [Description("Base URL of the active agent's OpenAI-compatible API (default depends on --agent: Unsloth 8888, LMStudio 1234).")]
    public string? Endpoint { get; set; }

    [CommandOption("--api-key")]
    [Description("Bearer API key for the active agent's server. Falls back to RedStar__Agents__<Agent>__ApiKey env var or appsettings.local.json.")]
    public string? ApiKey { get; set; }

    [CommandOption("--run-id")]
    [Description("Correlation ID tagged onto this run's OTel trace. Falls back to the REDSTAR_RUN_ID env var, then a generated GUID.")]
    public string? RunId { get; set; }
}