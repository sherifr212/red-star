using Marten;

using RedStar.Base;
using RedStar.Base.Agents.GoogleAI;
using RedStar.Base.Agents.LMStudio;
using RedStar.Base.Agents.Unsloth;

using Spectre.Console.Cli;

namespace RedStar.Cli.Commands;

/// <summary>
/// Also registered as the app's default command, so `redstar -p "hi"` and `redstar chat -p "hi"`
/// behave identically regardless of which other options are also present.
/// </summary>
public sealed class ChatCommand : AsyncCommand<ChatSettings>
{
    private readonly UnslothHttpClient _unslothHttpClient;
    private readonly LMStudioHttpClient _lmStudioHttpClient;
    private readonly GoogleAIHttpClient _googleAIHttpClient;
    private readonly IDocumentStore _documentStore;

    public ChatCommand(UnslothHttpClient unslothHttpClient, LMStudioHttpClient lmStudioHttpClient,
                       GoogleAIHttpClient googleAIHttpClient, IDocumentStore documentStore)
    {
        _unslothHttpClient = unslothHttpClient;
        _lmStudioHttpClient = lmStudioHttpClient;
        _googleAIHttpClient = googleAIHttpClient;
        _documentStore = documentStore;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, ChatSettings settings, CancellationToken cancellationToken)
    {
        var claudeCode = new ClaudeCodeOverrides(
            CliPath: settings.ClaudeCliPath,
            AuthMode: settings.ClaudeAuthMode,
            Bare: settings.ClaudeBare,
            ProcessMode: settings.ClaudeProcessMode,
            WorkingDirectory: settings.ClaudeWorkingDirectory,
            AllowedTools: settings.ClaudeAllowedTools,
            DisallowedTools: settings.ClaudeDisallowedTools,
            PermissionMode: settings.ClaudePermissionMode,
            MaxBudgetUsd: settings.ClaudeMaxBudgetUsd);

        var googleAI = new GoogleAIOverrides(
            ThinkingEffort: settings.ThinkingEffort,
            IncludeThoughts: settings.IncludeThoughts);

        var options = RedStarOptionsFactory.Build(
            settings.Agent, settings.Endpoint, settings.ApiKey, settings.Model, claudeCode, googleAI);
        return await ChatCommandHandler.RunAsync(
            options, settings.Prompt, settings.System, cancellationToken,
            unslothHttpClient: _unslothHttpClient,
            lmStudioHttpClient: _lmStudioHttpClient,
            googleAIHttpClient: _googleAIHttpClient,
            documentStore: _documentStore,
            runId: settings.RunId);
    }
}