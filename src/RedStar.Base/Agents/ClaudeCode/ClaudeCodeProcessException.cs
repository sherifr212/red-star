namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// Thrown when the <c>claude</c> subprocess fails to start, or exits non-zero. Carries the exit code and
/// captured stderr so <c>ChatCommandHandler</c>'s existing catch-and-print-the-error path (see
/// <c>ChatCommandHandler.SendAndPrintAsync</c>) surfaces something actionable -- e.g. an auth failure or a
/// bad <c>--allowedTools</c> value -- instead of a generic process-exited exception with no detail.
/// </summary>
public sealed class ClaudeCodeProcessException(int exitCode, string standardError)
    : Exception(BuildMessage(exitCode, standardError))
{
    public int ExitCode { get; } = exitCode;

    public string StandardError { get; } = standardError;

    private static string BuildMessage(int exitCode, string standardError) =>
        string.IsNullOrWhiteSpace(standardError)
            ? $"claude exited with code {exitCode} and no error output."
            : $"claude exited with code {exitCode}: {standardError.Trim()}";
}
