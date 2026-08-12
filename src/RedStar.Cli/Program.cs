using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RedStar.Base;
using RedStar.Cli;
using RedStar.Cli.Commands;
using RedStar.Cli.Infrastructure;
using RedStar.Cli.Telemetry;
using Spectre.Console.Cli;

// Windows consoles default to the system codepage (rarely UTF-8), so the box-drawing characters used
// throughout the chat UI (╭ ╮ ╰ ╯ │ ─) come out as literal '?'. Guarded because the setter throws
// IOException when there's no real console attached (piped/redirected output, some debuggers) --
// see the similar Console.IsOutputRedirected guard around AnsiConsole.Live in ChatCommandHandler.
if (!Console.IsOutputRedirected)
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
    }
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var services = new ServiceCollection();
services.AddHttpClient(AgentNames.Unsloth);
services.AddHttpClient(AgentNames.LMStudio);
services.AddTransient<ChatCommand>();
services.AddTransient<ModelsCommand>();
var registrar = new TypeRegistrar(services);

var app = new CommandApp<ChatCommand>(registrar);
app.Configure(config =>
{
    config.SetApplicationName("redstar");
    config.AddCommand<ChatCommand>("chat")
        .WithDescription("Chat with the locally hosted LLM (Unsloth Studio).");
    config.AddCommand<ModelsCommand>("models")
        .WithDescription("List models available on the server.");
});

var startupOptions = RedStarOptionsFactory.Build(agent: null, endpoint: null, apiKey: null);
using var telemetry = TelemetryBootstrapper.Configure(startupOptions);

return await app.RunAsync(args, cts.Token);
