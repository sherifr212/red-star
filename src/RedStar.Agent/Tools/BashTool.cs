using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace RedStar.Agent.Tools;

public sealed partial class BashTool
{
    private readonly ILogger<BashTool> _logger;

    public BashTool(ILogger<BashTool> logger)
    {
        _logger = logger;
    }

    [Description("Executes shell commands and scripts in your terminal environment (often handling system-level tasks, testing, and file operations).")]
    public async Task<string> RunBashAsync(
        [Description("The bash command to execute")] string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            LogBashError("Command cannot be null or empty.");
            return "Error: Command cannot be null or empty.";
        }

        try
        {
            LogBashCommandStarted(command);

            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                return "Error: Failed to start bash process.";
            }

            await process.StandardInput.WriteLineAsync(command);
            process.StandardInput.Close();

            using var cts = new System.Threading.CancellationTokenSource(System.TimeSpan.FromMinutes(1));

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            string output, error;
            try
            {
                await process.WaitForExitAsync(cts.Token);
                output = await outputTask;
                error = await errorTask;
            }
            catch (System.OperationCanceledException)
            {
                process.Kill();
                return "Error: Command execution timed out after 1 minute.";
            }

            LogBashCommandFinished(command, process.ExitCode);

            if (process.ExitCode != 0)
            {
                return $"Command exited with code {process.ExitCode}.\nOutput:\n{output}\nError:\n{error}";
            }

            return string.IsNullOrEmpty(output) ? "Command executed successfully with no output." : output;
        }
        catch (Exception ex)
        {
            LogBashException(ex, command);
            return $"Error executing command '{command}': {ex.Message}";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "BashTool error: {Error}")]
    private partial void LogBashError(string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "BashTool executing command: {Command}")]
    private partial void LogBashCommandStarted(string command);

    [LoggerMessage(Level = LogLevel.Information, Message = "BashTool finished command: {Command} with exit code: {ExitCode}")]
    private partial void LogBashCommandFinished(string command, int exitCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "BashTool exception executing command {Command}")]
    private partial void LogBashException(Exception ex, string command);
}
