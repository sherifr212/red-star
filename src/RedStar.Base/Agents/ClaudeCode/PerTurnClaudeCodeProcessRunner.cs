using System.Runtime.CompilerServices;

namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// <see cref="ClaudeCodeAgentOptions.ProcessMode"/> == <see cref="ClaudeCodeProcessModes.PerTurn"/> (the
/// default): every turn spawns a fresh <c>claude -p</c> process and lets it exit once its <c>result</c> line
/// arrives -- nothing persists between calls except the session id used to stitch turns together server-side.
/// The first turn passes <c>--session-id &lt;guid&gt;</c> (creating that session); every later turn passes
/// <c>--resume &lt;guid&gt;</c> instead -- verified against the real CLI (v2.1.224): reusing
/// <c>--session-id</c> on a second call errors <c>"Session ID ... is already in use"</c>, while
/// <c>--resume</c> correctly continues the same conversation.
/// </summary>
public sealed class PerTurnClaudeCodeProcessRunner(ClaudeCodeAgentOptions options, string modelId) : IClaudeCodeProcessRunner
{
    private readonly string _sessionId = Guid.NewGuid().ToString();
    private bool _hasSentFirstTurn;

    public async IAsyncEnumerable<string> SendAsync(
        string userText, string? instructions, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userText);

        var isFirstTurn = !_hasSentFirstTurn;
        _hasSentFirstTurn = true;

        var arguments = ClaudeCodeProcessArguments.BuildCommonFlags(options, modelId);
        if (isFirstTurn)
        {
            arguments.Add("--session-id");
            arguments.Add(_sessionId);

            if (!string.IsNullOrEmpty(instructions))
            {
                arguments.Add("--append-system-prompt");
                arguments.Add(instructions);
            }
        }
        else
        {
            arguments.Add("--resume");
            arguments.Add(_sessionId);
        }

        arguments.Add(userText);

        var (process, standardErrorTask) = ClaudeCodeProcessLauncher.Start(options, arguments, redirectStandardInput: false);
        using (process)
        {
            using var registration = cancellationToken.Register(() => ClaudeCodeProcessLauncher.TryKill(process));

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                yield return line;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var standardError = await standardErrorTask.ConfigureAwait(false);
                throw new ClaudeCodeProcessException(process.ExitCode, standardError);
            }
        }
    }

    /// <summary>Nothing persists between calls in this mode -- each turn's process is already fully
    /// awaited/disposed by the time <see cref="SendAsync"/> returns.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
