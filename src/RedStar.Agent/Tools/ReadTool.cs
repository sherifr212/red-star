using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace RedStar.Agent.Tools;

public sealed partial class ReadTool
{
    private readonly ILogger<ReadTool> _logger;

    public ReadTool(ILogger<ReadTool> logger)
    {
        _logger = logger;
    }

    [Description("Inspects and reads the contents of files in your workspace.")]
    public async Task<string> ReadAsync(
        [Description("The path to the file to read")] string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            LogReadError(filePath, "File path cannot be null or empty.");
            return "Error: File path cannot be null or empty.";
        }

        try
        {
            if (!File.Exists(filePath))
            {
                LogFileNotFound(filePath);
                return $"Error: File '{filePath}' not found.";
            }

            var content = await File.ReadAllTextAsync(filePath);
            LogFileReadSuccess(filePath);
            return content;
        }
        catch (Exception ex)
        {
            LogReadException(ex, filePath);
            return $"Error reading file '{filePath}': {ex.Message}";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ReadTool error for file {FilePath}: {Error}")]
    private partial void LogReadError(string filePath, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ReadTool file not found: {FilePath}")]
    private partial void LogFileNotFound(string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "ReadTool successfully read file: {FilePath}")]
    private partial void LogFileReadSuccess(string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "ReadTool exception reading file {FilePath}")]
    private partial void LogReadException(Exception ex, string filePath);
}
