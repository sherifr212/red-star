using Microsoft.Extensions.Logging;
using RedStar.Base;
using RedStar.Base.Agents.ClaudeCode;
using RedStar.Base.Agents.GoogleAI;
using RedStar.Base.Agents.LMStudio;
using RedStar.Base.Telemetry;
using Spectre.Console;

namespace RedStar.Cli;

internal static class ModelsCommandHandler
{
    /// <param name="httpClientFactory">
    /// Factory for creating pre-configured HttpClient instances per agent. Only null in tests, which always
    /// supply modelsClientFactory directly.
    /// </param>
    /// <param name="modelsClientFactory">
    /// Builds the <see cref="IModelsClient"/> to query. Defaults to a real <see cref="ModelsClient"/> or
    /// <c>LMStudioModelsClient</c> depending on <see cref="RedStarOptions.Agent"/> (same per-agent switch
    /// as <see cref="ChatCommandHandler.RunAsync"/>'s <c>modelsClientFactory</c> default); tests can
    /// substitute a fake here without touching the network.
    /// </param>
    /// <param name="runId">
    /// Correlation ID tagged onto this run's root OTel span (<c>run.correlation.id</c>). Falls back to the
    /// <c>REDSTAR_RUN_ID</c> environment variable, then a generated GUID. See the identical parameter on
    /// <see cref="ChatCommandHandler.RunAsync"/> for the full rationale.
    /// </param>
    public static async Task<int> RunAsync(
        RedStarOptions options,
        CancellationToken cancellationToken,
        IHttpClientFactory? httpClientFactory = null,
        Func<RedStarOptions, IModelsClient>? modelsClientFactory = null,
        string? runId = null)
    {
        using var activity = RedStarTelemetry.ActivitySource.StartActivity("redstar.models");
        runId ??= Environment.GetEnvironmentVariable("REDSTAR_RUN_ID") ?? Guid.NewGuid().ToString("N");
        activity?.SetTag("run.correlation.id", runId);

        var logger = RedStarTelemetry.CreateLogger("RedStar.Cli.ModelsCommandHandler");
        logger.LogInformation("Starting redstar models run {RunId}", runId);

        var isLMStudio = string.Equals(options.Agent, AgentNames.LMStudio, StringComparison.OrdinalIgnoreCase);
        var isClaudeCode = string.Equals(options.Agent, AgentNames.ClaudeCode, StringComparison.OrdinalIgnoreCase);
        var isGoogleAI = string.Equals(options.Agent, AgentNames.GoogleAI, StringComparison.OrdinalIgnoreCase);

        // httpClientFactory is only null in tests, which always supply modelsClientFactory directly and so
        // never evaluate these lambdas; production always resolves ModelsCommand through DI (see Program.cs),
        // so httpClientFactory is non-null whenever these bodies actually run. ClaudeCode is a subprocess
        // agent, not an HTTP one, so it never touches httpClientFactory.
        modelsClientFactory ??= isClaudeCode
            ? static opts => new ClaudeCodeModelsClient()
            : isGoogleAI
                ? opts => new GoogleAIModelsClient(httpClientFactory!.CreateClient(AgentNames.GoogleAI), opts)
                : isLMStudio
                    ? opts => new LMStudioModelsClient(httpClientFactory!.CreateClient(AgentNames.LMStudio), opts)
                    : opts => new ModelsClient(httpClientFactory!.CreateClient(AgentNames.Unsloth), opts);

        var modelsClient = modelsClientFactory(options);
        try
        {
            var models = await modelsClient.ListAsync(cancellationToken);
            if (models.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No models available.[/] Load one on the server first.");
                return 0;
            }

            IModelTableRenderer renderer = isLMStudio ? new LMStudioModelTableRenderer() : new DefaultModelTableRenderer();
            var table = renderer.Render(models);

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run {RunId} failed to list models", runId);
            ConsoleOutput.Error.MarkupLine($"[red]Error listing models: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    internal static string FormatDetails(ModelInfo model)
    {
        var parts = new List<string>();
        if (model.Type is { Length: > 0 })
        {
            parts.Add(model.Type);
        }

        if (model.MaxContextLength is { } contextLength)
        {
            parts.Add($"{contextLength} ctx");
        }

        if (model.Quantization is { Length: > 0 })
        {
            parts.Add(model.Quantization);
        }

        return string.Join(" · ", parts);
    }
}

internal interface IModelTableRenderer
{
    Table Render(IReadOnlyList<ModelInfo> models);
}

internal sealed class DefaultModelTableRenderer : IModelTableRenderer
{
    public Table Render(IReadOnlyList<ModelInfo> models)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(string.Empty);
        table.AddColumn("Model");
        table.AddColumn("Details");
        foreach (var model in models)
        {
            var id = Markup.Escape(model.Id);
            table.AddRow(
                model.Loaded ? "[green]●[/]" : string.Empty,
                model.Loaded ? $"[green]{id}[/] [dim](loaded)[/]" : id,
                Markup.Escape(ModelsCommandHandler.FormatDetails(model)));
        }
        return table;
    }
}

internal sealed class LMStudioModelTableRenderer : IModelTableRenderer
{
    public Table Render(IReadOnlyList<ModelInfo> models)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn(string.Empty);
        table.AddColumn("Model");
        table.AddColumn("Publisher");
        table.AddColumn("Architecture");
        table.AddColumn("Format");
        table.AddColumn("Details");

        foreach (var model in models)
        {
            var lmModel = model as LMStudioModelInfo;
            var id = Markup.Escape(model.Id);
            table.AddRow(
                model.Loaded ? "[green]●[/]" : string.Empty,
                model.Loaded ? $"[green]{id}[/] [dim](loaded)[/]" : id,
                Markup.Escape(lmModel?.Publisher ?? "-"),
                Markup.Escape(lmModel?.Architecture ?? "-"),
                Markup.Escape(lmModel?.Format ?? "-"),
                Markup.Escape(ModelsCommandHandler.FormatDetails(model)));
        }
        return table;
    }
}
