using System.ComponentModel;
using Spectre.Console.Cli;

namespace RedStar.Cli.Commands;

public sealed class ChatSettings : CommonSettings
{
    [CommandOption("-m|--model")]
    [Description("Model id to use for this call. Overrides the configured default model " +
                 "(RedStar__Agents__Unsloth__DefaultModel) and auto-detection.")]
    public string? Model { get; set; }

    [CommandOption("-p|--prompt")]
    [Description("Send a single prompt and print the response, then exit. Omit for an interactive session.")]
    public string? Prompt { get; set; }

    [CommandOption("-s|--system")]
    [Description("Optional system prompt to prime the conversation.")]
    public string? System { get; set; }
}
