using RedStar.Base.Agents.GoogleAI;
using RedStar.Base.Agents.LMStudio;
using RedStar.Base.Agents.Unsloth;

using Spectre.Console.Cli;

namespace RedStar.Cli.Commands;

public sealed class ModelsCommand : AsyncCommand<CommonSettings>
{
    private readonly UnslothHttpClient _unslothHttpClient;
    private readonly LMStudioHttpClient _lmStudioHttpClient;
    private readonly GoogleAIHttpClient _googleAIHttpClient;

    public ModelsCommand(UnslothHttpClient unslothHttpClient, LMStudioHttpClient lmStudioHttpClient, GoogleAIHttpClient googleAIHttpClient)
    {
        _unslothHttpClient = unslothHttpClient;
        _lmStudioHttpClient = lmStudioHttpClient;
        _googleAIHttpClient = googleAIHttpClient;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        var options = RedStarOptionsFactory.Build(settings.Agent, settings.Endpoint, settings.ApiKey);
        return await ModelsCommandHandler.RunAsync(
            options, cancellationToken,
            unslothHttpClient: _unslothHttpClient,
            lmStudioHttpClient: _lmStudioHttpClient,
            googleAIHttpClient: _googleAIHttpClient,
            runId: settings.RunId);
    }
}