using System;

using Spectre.Console;

namespace RedStar.Cli.Rendering;

internal static class ChatEngineConsoleHelper
{
    /// <summary>Rows a <see cref="StageBox"/>'s <see cref="Panel"/> chrome (top border, bottom border,
    /// footer row) always costs beyond its body -- used together with <see cref="GetSafeBoxHeight"/> to
    /// decide when a still-growing box needs to seal early.</summary>
    internal const int PanelChromeLines = 3;

    /// <summary>How tall a live-redrawn box is allowed to get before it's sealed and a same-stage
    /// continuation box opens in its place, instead of letting <see cref="AnsiConsole.Live"/>'s default
    /// overflow handling crop it. Spectre's <c>Live</c> region defaults to
    /// <c>VerticalOverflow.Ellipsis</c>/<c>VerticalOverflowCropping.Top</c>: once a live-redrawn panel's
    /// height exceeds the console's row count, it silently drops the top lines and shows only the tail --
    /// those dropped lines are never written to the terminal at all while the box is still live (Spectre
    /// only force-writes the full content once, when the <c>Live</c> region closes). That means scrolling
    /// up past a still-streaming box's on-screen footprint shows whatever was already there before it (the
    /// previous box), not the current box's earlier content, since the current box never actually grew
    /// into that screen space yet. Staying comfortably under the console's height (a few rows of margin,
    /// floor of 6 for very short windows) means every box lands fully in real, permanent scrollback as
    /// it's sealed, and Spectre's crop path never needs to trigger.</summary>
    internal static int GetSafeBoxHeight() => Math.Max(6, Console.WindowHeight - 3);

    public static int GetBoxWidth() => Math.Clamp(Console.WindowWidth - 2, 20, 100);

    public static void PrintBoxTopBorder(int width, string label, Color color)
    {
        var title = $" {label} ";
        var dashes = Math.Max(2, width - 2 - title.Length);
        var left = dashes / 2;
        var right = dashes - left;
        AnsiConsole.MarkupLine(
            $"[{color.ToMarkup()}]╭{new string('─', left)}[/]{Markup.Escape(title)}[{color.ToMarkup()}]{new string('─', right)}╮[/]");
    }

    public static void PrintBoxBottomBorder(int width, Color color) =>
        AnsiConsole.MarkupLine($"[{color.ToMarkup()}]╰{new string('─', width - 2)}╯[/]");

    public static string? ReadUserMessageBoxed()
    {
        var width = GetBoxWidth();
        PrintBoxTopBorder(width, "You", Color.Cyan1);
        AnsiConsole.Markup($"[{Color.Cyan1.ToMarkup()}]│[/] > ");
        var line = Console.ReadLine();
        PrintBoxBottomBorder(width, Color.Cyan1);
        return line;
    }

    public static void PrintUserMessageBox(string text)
    {
        var panel = new Panel(Markup.Escape(text))
            .Header("[cyan]You[/]")
            .RoundedBorder()
            .BorderColor(Color.Cyan1)
            .Expand();
        AnsiConsole.Write(panel);
    }
}