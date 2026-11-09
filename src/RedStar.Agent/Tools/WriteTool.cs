using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace RedStar.Agent.Tools;

public sealed partial class WriteTool
{
    private readonly ILogger<WriteTool> _logger;

    public WriteTool(ILogger<WriteTool> logger)
    {
        _logger = logger;
    }

    [Description("Creates and writes new files from scratch with generating changes diffs by default capability.")]
    public async Task<string> WriteAsync(
        [Description("The path to the file to write")] string filePath,
        [Description("The content to write to the file")] string content)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            LogWriteError(filePath, "File path cannot be null or empty.");
            return "Error: File path cannot be null or empty.";
        }

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                LogDirectoryCreated(directory);
            }

            var isNewFile = !File.Exists(filePath);

            // Note: Since diff tool implementations would require a diff library,
            // a simple representation of the change is to just return a message that it changed.
            // If the user wants an actual diff patch generated, we could output the old vs new content.
            string diffOutput = "";
            if (!isNewFile)
            {
                 var oldContent = await File.ReadAllTextAsync(filePath);
                 if (oldContent != content)
                 {
                     var oldSlice = oldContent.Length > 20 ? oldContent.AsSpan(0, 20) : oldContent.AsSpan();
                     var newSlice = content.Length > 20 ? content.AsSpan(0, 20) : content.AsSpan();

                     diffOutput = "Diff:\n- " + new string(oldSlice) + (oldContent.Length > 20 ? "..." : "")
                                + "\n+ " + new string(newSlice) + (content.Length > 20 ? "..." : "") + "\n";
                 }
                 else
                 {
                     diffOutput = "No changes detected.\n";
                 }
            }

            await File.WriteAllTextAsync(filePath, content ?? string.Empty);

            LogFileWriteSuccess(filePath, isNewFile);
            return $"Successfully {(isNewFile ? "created" : "overwritten")} file '{filePath}'.\n{diffOutput}";
        }
        catch (Exception ex)
        {
            LogWriteException(ex, filePath);
            return $"Error writing file '{filePath}': {ex.Message}";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "WriteTool error for file {FilePath}: {Error}")]
    private partial void LogWriteError(string filePath, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "WriteTool created directory: {Directory}")]
    private partial void LogDirectoryCreated(string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "WriteTool successfully wrote file: {FilePath} (IsNew: {IsNewFile})")]
    private partial void LogFileWriteSuccess(string filePath, bool isNewFile);

    [LoggerMessage(Level = LogLevel.Error, Message = "WriteTool exception writing file {FilePath}")]
    private partial void LogWriteException(Exception ex, string filePath);
}
