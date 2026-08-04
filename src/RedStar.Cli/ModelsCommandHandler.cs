using Microsoft.Extensions.Logging;
using RedStar.Base;
using RedStar.Base.Telemetry;
using Spectre.Console;

namespace RedStar.Cli;

internal static class ModelsCommandHandler
{
    /// <param name="modelsClientFactory">
    /// Builds the <see cref="IModelsClient"/> to query. Defaults to a real <see cref="ModelsClient"/>;
    /// tests can substitute a fake here without touching the network.
    /// </param>
    /// <param name="runId">
    /// Correlation ID tagged onto this run's root OTel span (<c>run.correlation.id</c>). Falls back to the
    /// <c>REDSTAR_RUN_ID</c> environment variable, then a generated GUID. See the identical parameter on
    /// <see cref="ChatCommandHandler.RunAsync"/> for the full rationale.
    /// </param>
    public static async Task<int> RunAsync(
        RedStarOptions options,
        CancellationToken cancellationToken,
        Func<RedStarOptions, IModelsClient>? modelsClientFactory = null,
        string? runId = null)
    {
        using var activity = RedStarTelemetry.ActivitySource.StartActivity("redstar.models");
        runId ??= Environment.GetEnvironmentVariable("REDSTAR_RUN_ID") ?? Guid.NewGuid().ToString("N");
        activity?.SetTag("run.correlation.id", runId);

        var logger = RedStarTelemetry.CreateLogger("RedStar.Cli.ModelsCommandHandler");
        logger.LogInformation("Starting redstar models run {RunId}", runId);

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
            logger.LogError(ex, "Run {RunId} failed to list models", runId);
            ConsoleOutput.Error.MarkupLine($"[red]Error listing models: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
        finally
        {
            (modelsClient as IDisposable)?.Dispose();
        }
    }
}
