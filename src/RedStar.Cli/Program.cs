using RedStar.Cli.Commands;
using Spectre.Console.Cli;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var app = new CommandApp<ChatCommand>();
app.Configure(config =>
{
    config.SetApplicationName("redstar");
    config.AddCommand<ChatCommand>("chat")
        .WithDescription("Chat with the locally hosted LLM (Unsloth Studio).");
    config.AddCommand<ModelsCommand>("models")
        .WithDescription("List models available on the server.");
});

return await app.RunAsync(args, cts.Token);
