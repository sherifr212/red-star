using System.ComponentModel;
using Spectre.Console.Cli;

namespace RedStar.Cli.Commands;

/// <summary>Options shared by every subcommand (and the default 'chat' root behavior).</summary>
public class CommonSettings : CommandSettings
{
    [CommandOption("--endpoint")]
    [Description("Base URL of the OpenAI-compatible API (default: http://127.0.0.1:8888/v1).")]
    public string? Endpoint { get; set; }

    [CommandOption("--api-key")]
    [Description("Bearer API key for the server. Falls back to RedStar__ApiKey env var or appsettings.local.json.")]
    public string? ApiKey { get; set; }

    [CommandOption("--run-id")]
    [Description("Correlation ID tagged onto this run's OTel trace. Falls back to the REDSTAR_RUN_ID env var, then a generated GUID.")]
    public string? RunId { get; set; }
}
