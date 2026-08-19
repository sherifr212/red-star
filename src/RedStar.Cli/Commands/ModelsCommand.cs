using Spectre.Console.Cli;

namespace RedStar.Cli.Commands;

public sealed class ModelsCommand : AsyncCommand<CommonSettings>
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ModelsCommand(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, CommonSettings settings, CancellationToken cancellationToken)
    {
        var options = RedStarOptionsFactory.Build(settings.Agent, settings.Endpoint, settings.ApiKey);
        return await ModelsCommandHandler.RunAsync(options, cancellationToken, httpClientFactory: _httpClientFactory, runId: settings.RunId);
    }
}