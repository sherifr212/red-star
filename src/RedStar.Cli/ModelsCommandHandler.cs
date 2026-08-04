using RedStar.Base;
using Spectre.Console;

namespace RedStar.Cli;

internal static class ModelsCommandHandler
{
    /// <param name="modelsClientFactory">
    /// Builds the <see cref="IModelsClient"/> to query. Defaults to a real <see cref="ModelsClient"/>;
    /// tests can substitute a fake here without touching the network.
    /// </param>
    public static async Task<int> RunAsync(
        RedStarOptions options,
        CancellationToken cancellationToken,
        Func<RedStarOptions, IModelsClient>? modelsClientFactory = null)
    {
        var modelsClient = modelsClientFactory is null ? new ModelsClient(options) : modelsClientFactory(options);
        try
        {
            var models = await modelsClient.ListAsync(cancellationToken);
            if (models.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No models available.[/] Load one in Unsloth Studio first.");
                return 0;
            }

            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn(string.Empty);
            table.AddColumn("Model");
            foreach (var model in models)
            {
                var id = Markup.Escape(model.Id);
                table.AddRow(
                    model.Loaded ? "[green]●[/]" : string.Empty,
                    model.Loaded ? $"[green]{id}[/] [dim](loaded)[/]" : id);
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleOutput.Error.MarkupLine($"[red]Error listing models: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
        finally
        {
            (modelsClient as IDisposable)?.Dispose();
        }
    }
}
