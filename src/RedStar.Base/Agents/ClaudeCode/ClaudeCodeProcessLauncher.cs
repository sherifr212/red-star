using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// Spawns the <c>claude</c> subprocess with the environment/working-directory/stdio wiring both
/// <see cref="PerTurnClaudeCodeProcessRunner"/> and <see cref="LongLivedClaudeCodeProcessRunner"/> need.
/// </summary>
internal static class ClaudeCodeProcessLauncher
{
    /// <summary>
    /// Starts the process with stdout/stderr always redirected (stderr is drained concurrently into
    /// <c>StandardErrorTask</c> from the moment the process starts, so a chatty stderr stream can never fill
    /// its OS pipe buffer and deadlock the child -- callers must not also read <c>Process.StandardError</c>
    /// directly). <paramref name="redirectStandardInput"/> is only true for
    /// <see cref="LongLivedClaudeCodeProcessRunner"/>, which needs to write stream-json input lines;
    /// <see cref="PerTurnClaudeCodeProcessRunner"/> passes its one prompt as a trailing CLI argument instead
    /// and needs no stdin pipe at all.
    /// </summary>
    public static (Process Process, Task<string> StandardErrorTask) Start(
        ClaudeCodeAgentOptions options, IReadOnlyList<string> arguments, bool redirectStandardInput)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = options.CliPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectStandardInput,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (redirectStandardInput)
        {
            startInfo.StandardInputEncoding = Encoding.UTF8;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrEmpty(options.WorkingDirectory))
        {
            startInfo.WorkingDirectory = options.WorkingDirectory;
        }

        // AuthMode.CliLogin (the default) deliberately sets nothing here -- the spawned process inherits
        // RedStar's own environment as-is and authenticates via whatever `claude auth login` already set up
        // on this machine (OAuth credentials in the system keychain). Only AuthMode.ApiKey injects a
        // credential explicitly; there is no --api-key CLI flag for this on the real CLI.
        if (string.Equals(options.AuthMode, ClaudeCodeAuthModes.ApiKey, StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Environment["ANTHROPIC_API_KEY"] = options.ApiKey;
        }

        var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new ClaudeCodeProcessException(
                -1, $"Could not start '{options.CliPath}': {ex.Message}. Check RedStar:Agents:ClaudeCode:CliPath " +
                     "(or --claude-cli-path) points at a valid executable on PATH.");
        }

        var standardErrorTask = process.StandardError.ReadToEndAsync();
        return (process, standardErrorTask);
    }

    /// <summary>Best-effort kill of the whole process tree (so a still-running child claude spawned by the
    /// CLI itself, e.g. a Bash tool call, doesn't survive its parent). Swallows every failure -- the process
    /// may have already exited between the <see cref="Process.HasExited"/> check and <see cref="Process.Kill"/>
    /// (benign race with normal completion), or disposal may already be underway.</summary>
    public static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort; see remarks above.
        }
    }
}
