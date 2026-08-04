using Spectre.Console;

namespace RedStar.Cli;

/// <summary>
/// AnsiConsole.Console writes to stdout only. Errors/warnings in this CLI have always gone to
/// stderr, so this gives them a markup-capable console pointed at Console.Error instead.
/// </summary>
internal static class ConsoleOutput
{
    public static readonly IAnsiConsole Error = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error),
    });
}
