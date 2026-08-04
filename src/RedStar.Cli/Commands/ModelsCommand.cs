using Spectre.Console.Cli;

namespace RedStar.Cli.Commands;

public sealed class ModelsCommand : AsyncCommand<CommonSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings)
    {
        var options = RedStarOptionsFactory.Build(settings.Endpoint, settings.ApiKey);
        return await ModelsCommandHandler.RunAsync(options, CliCancellation.Token);
    }
}
