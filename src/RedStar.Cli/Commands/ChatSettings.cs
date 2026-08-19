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

    // --- ClaudeCode-only options below. No effect for --agent Unsloth/LMStudio. Each mirrors a
    // RedStar:Agents:ClaudeCode:* config key/RedStar__Agents__ClaudeCode__* env var one-for-one -- see
    // RedStarOptionsFactory.Build and ClaudeCodeOverrides.

    [CommandOption("--claude-cli-path")]
    [Description("ClaudeCode only: executable to spawn. Overrides RedStar__Agents__ClaudeCode__CliPath (default: \"claude\", resolved via PATH).")]
    public string? ClaudeCliPath { get; set; }

    [CommandOption("--claude-auth-mode")]
    [Description("ClaudeCode only: \"CliLogin\" (use a locally logged-in `claude auth login` credential, default) or \"ApiKey\" (use --api-key/ANTHROPIC_API_KEY instead).")]
    public string? ClaudeAuthMode { get; set; }

    [CommandOption("--claude-bare")]
    [Description("ClaudeCode only: pass --bare to the CLI (skips hooks/plugins/CLAUDE.md auto-discovery and OAuth keychain reads).")]
    public bool? ClaudeBare { get; set; }

    [CommandOption("--claude-process-mode")]
    [Description("ClaudeCode only: \"PerTurn\" (spawn a fresh process every turn, default) or \"LongLived\" (keep one process alive for the whole session).")]
    public string? ClaudeProcessMode { get; set; }

    [CommandOption("--claude-working-dir")]
    [Description("ClaudeCode only: working directory for the spawned process. Left unset, RedStar's own current directory is inherited.")]
    public string? ClaudeWorkingDirectory { get; set; }

    [CommandOption("--claude-allowed-tools")]
    [Description("ClaudeCode only: tool names to pass via --allowedTools (e.g. Read Grep \"Bash(git *)\"). Empty/unset means no tools are allowed.")]
    public string[]? ClaudeAllowedTools { get; set; }

    [CommandOption("--claude-disallowed-tools")]
    [Description("ClaudeCode only: tool names to pass via --disallowedTools, same syntax as --claude-allowed-tools.")]
    public string[]? ClaudeDisallowedTools { get; set; }

    [CommandOption("--claude-permission-mode")]
    [Description("ClaudeCode only: passed as --permission-mode (acceptEdits/auto/bypassPermissions/manual/dontAsk/plan). Unset omits the flag.")]
    public string? ClaudePermissionMode { get; set; }

    [CommandOption("--claude-max-budget-usd")]
    [Description("ClaudeCode only: passed as --max-budget-usd. Unset means no budget cap.")]
    public double? ClaudeMaxBudgetUsd { get; set; }

    // --- GoogleAI-only options below. No effect for --agent Unsloth/LMStudio/ClaudeCode. Each mirrors a
    // RedStar:Agents:GoogleAI:* config key/RedStar__Agents__GoogleAI__* env var one-for-one -- see
    // RedStarOptionsFactory.Build and GoogleAIOverrides.

    [CommandOption("--thinking-effort")]
    [Description("GoogleAI only: Gemini \"thinking mode\" effort -- None/Low/Medium/High, case-insensitive. Overrides RedStar__Agents__GoogleAI__ThinkingEffort (default: unset, i.e. the model's own default).")]
    public string? ThinkingEffort { get; set; }

    [CommandOption("--include-thoughts")]
    [Description("GoogleAI only: whether Gemini's thought/reasoning trace is requested and surfaced as its own \"Reasoning\" box. Overrides RedStar__Agents__GoogleAI__IncludeThoughts (default: true).")]
    public bool? IncludeThoughts { get; set; }
}