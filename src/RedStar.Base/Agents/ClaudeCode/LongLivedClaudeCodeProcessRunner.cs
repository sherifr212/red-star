using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// <see cref="ClaudeCodeAgentOptions.ProcessMode"/> == <see cref="ClaudeCodeProcessModes.LongLived"/>: one
/// <c>claude -p --input-format stream-json</c> process is spawned lazily on the first turn and kept alive for
/// the rest of the <see cref="ChatSession"/>, fed one JSON input line per turn over its still-open stdin pipe
/// instead of being re-spawned. Needs no <c>--session-id</c>/<c>--resume</c> at all -- verified against the
/// real CLI (v2.1.224): piping two queued <c>{"type":"user",...}</c> lines into one
/// <c>--input-format stream-json</c> invocation produced two full turns (two <c>result</c> lines) with
/// context correctly carried between them, since the live process itself <em>is</em> the session.
///
/// Only one turn is ever in flight at a time in practice (<c>ChatSession</c>'s callers await each turn before
/// starting the next), but <see cref="_gate"/> still serializes <see cref="SendAsync"/> defensively rather
/// than assuming that.
/// </summary>
public sealed class LongLivedClaudeCodeProcessRunner(ClaudeCodeAgentOptions options, string modelId) : IClaudeCodeProcessRunner
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private Task<string>? _standardErrorTask;
    private CancellationTokenRegistration _cancellationRegistration;

    public async IAsyncEnumerable<string> SendAsync(
        string userText, string? instructions, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userText);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = EnsureStarted(instructions, cancellationToken);

            var inputLine = JsonSerializer.Serialize(new ClaudeCodeStreamJsonInput("user", new ClaudeCodeStreamJsonMessage("user", userText)));
            await process.StandardInput.WriteLineAsync(inputLine.AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                yield return line;

                if (ClaudeCodeStreamJsonParser.TryParseLine(line) is { Result: not null })
                {
                    yield break; // this turn is complete; the process stays alive for the next one.
                }
            }

            // Stdout ended with no result line for this turn -- the process exited unexpectedly mid-turn.
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var standardError = await _standardErrorTask!.ConfigureAwait(false);
            throw new ClaudeCodeProcessException(process.ExitCode, standardError);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Process EnsureStarted(string? instructions, CancellationToken cancellationToken)
    {
        if (_process is { } existing)
        {
            return existing;
        }

        var arguments = ClaudeCodeProcessArguments.BuildCommonFlags(options, modelId);
        arguments.Add("--input-format");
        arguments.Add("stream-json");

        if (!string.IsNullOrEmpty(instructions))
        {
            arguments.Add("--append-system-prompt");
            arguments.Add(instructions);
        }

        var (process, standardErrorTask) = ClaudeCodeProcessLauncher.Start(options, arguments, redirectStandardInput: true);
        _process = process;
        _standardErrorTask = standardErrorTask;

        // Best-effort cleanup so this subprocess doesn't outlive RedStar -- see the remarks on
        // ClaudeCodeAgentOptions.ProcessMode. Ctrl+C fires the cancellation registration (the same token
        // flows through every turn, so registering once on the first call covers the whole run); normal
        // process exit (one-shot completing, or "exit"/"quit" ending the interactive loop) fires
        // ProcessExit instead, since ChatCommandHandler never explicitly disposes the chat client today.
        // Neither is bulletproof against a hard kill of RedStar's own process, but between the two this
        // covers every realistic shutdown path.
        _cancellationRegistration = cancellationToken.Register(() => ClaudeCodeProcessLauncher.TryKill(process));
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        return process;
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        if (_process is { } process)
        {
            ClaudeCodeProcessLauncher.TryKill(process);
        }
    }

    public async ValueTask DisposeAsync()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        await _cancellationRegistration.DisposeAsync().ConfigureAwait(false);

        if (_process is { } process)
        {
            ClaudeCodeProcessLauncher.TryKill(process);
            try
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort -- we're already tearing this down.
            }

            process.Dispose();
        }

        _gate.Dispose();
    }

    /// <summary>Shape verified against the real CLI: <c>{"type":"user","message":{"role":"user","content":"..."}}</c>
    /// -- <see cref="JsonPropertyNameAttribute"/> pins the exact lowercase field names the protocol requires,
    /// since plain <see cref="JsonSerializer.Serialize{TValue}(TValue, JsonSerializerOptions?)"/> with no
    /// naming policy would otherwise emit the C# property names verbatim (PascalCase).</summary>
    private sealed record ClaudeCodeStreamJsonInput(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("message")] ClaudeCodeStreamJsonMessage Message);

    private sealed record ClaudeCodeStreamJsonMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
