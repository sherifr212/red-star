using Spectre.Console.Cli;

namespace RedStar.Cli.Commands;

public sealed class ModelsCommand : AsyncCommand<CommonSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        var options = RedStarOptionsFactory.Build(settings.Agent, settings.Endpoint, settings.ApiKey);
        return await ModelsCommandHandler.RunAsync(options, cancellationToken, runId: settings.RunId);
    }
}
