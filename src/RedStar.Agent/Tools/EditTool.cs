using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace RedStar.Agent.Tools;

public sealed partial class EditTool
{
    private readonly ILogger<EditTool> _logger;

    public EditTool(ILogger<EditTool> logger)
    {
        _logger = logger;
    }

    [Description("Modifies existing files with targeted updates or patches by replacing search string with replace string.")]
    public async Task<string> EditAsync(
        [Description("The path to the file to edit")] string filePath,
        [Description("The string to search for in the file")] string searchString,
        [Description("The string to replace it with")] string replaceString)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            LogEditError(filePath, "File path cannot be null or empty.");
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

            if (string.IsNullOrEmpty(searchString))
            {
                 return "Error: Search string cannot be empty.";
            }

            if (!content.Contains(searchString))
            {
                LogSearchStringNotFound(filePath);
                return $"Error: Search string not found in file '{filePath}'.";
            }

            var newContent = content.Replace(searchString, replaceString);
            await File.WriteAllTextAsync(filePath, newContent);

            LogFileEditSuccess(filePath);
            return $"Successfully edited file '{filePath}'.";
        }
        catch (Exception ex)
        {
            LogEditException(ex, filePath);
            return $"Error editing file '{filePath}': {ex.Message}";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "EditTool error for file {FilePath}: {Error}")]
    private partial void LogEditError(string filePath, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "EditTool file not found: {FilePath}")]
    private partial void LogFileNotFound(string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "EditTool search string not found in file: {FilePath}")]
    private partial void LogSearchStringNotFound(string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "EditTool successfully edited file: {FilePath}")]
    private partial void LogFileEditSuccess(string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "EditTool exception editing file {FilePath}")]
    private partial void LogEditException(Exception ex, string filePath);
}
