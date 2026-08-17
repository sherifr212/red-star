using Spectre.Console;
using RedStar.Base.Agents.LMStudio;
using RedStar.Base.Agents.Unsloth;
using RedStar.Base;
using System.Collections.Generic;

namespace RedStar.Cli.Rendering;

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
