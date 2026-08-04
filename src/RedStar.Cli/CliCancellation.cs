namespace RedStar.Cli;

/// <summary>
/// Spectre.Console.Cli's <c>AsyncCommand.ExecuteAsync</c> doesn't accept a <see cref="CancellationToken"/>,
/// unlike System.CommandLine's action delegates which got one wired to Ctrl+C automatically. This replaces
/// that: <see cref="Initialize"/> hooks Ctrl+C once at startup and commands read <see cref="Token"/>.
/// </summary>
internal static class CliCancellation
{
    private static readonly CancellationTokenSource Source = new();

    public static CancellationToken Token => Source.Token;

    public static void Initialize()
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Source.Cancel();
        };
    }
}
