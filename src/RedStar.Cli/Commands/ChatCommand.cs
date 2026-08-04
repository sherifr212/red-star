using Spectre.Console.Cli;

namespace RedStar.Cli.Commands;

/// <summary>
/// Also registered as the app's default command, so `redstar -p "hi"` and `redstar chat -p "hi"`
/// behave identically regardless of which other options are also present.
/// </summary>
public sealed class ChatCommand : AsyncCommand<ChatSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ChatSettings settings)
    {
        var options = RedStarOptionsFactory.Build(settings.Endpoint, settings.ApiKey, settings.Model);
        return await ChatCommandHandler.RunAsync(options, settings.Prompt, settings.System, CliCancellation.Token);
    }
}
