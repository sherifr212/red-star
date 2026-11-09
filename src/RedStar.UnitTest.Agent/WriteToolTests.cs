using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RedStar.Agent.Tools;
using Xunit;

namespace RedStar.UnitTest.Agent;

public class WriteToolTests : IDisposable
{
    private readonly WriteTool _tool;
    private readonly string _testFilePath;

    public WriteToolTests()
    {
        _tool = new WriteTool(NullLogger<WriteTool>.Instance);
        _testFilePath = Path.Combine(Path.GetTempPath(), $"write_test_{Guid.NewGuid()}.txt");
    }

    [Fact]
    public async Task WriteAsync_CreatesNewFile_WhenFileDoesNotExist()
    {
        // Arrange
        var content = "New content!";

        // Act
        var result = await _tool.WriteAsync(_testFilePath, content);

        // Assert
        Assert.True(File.Exists(_testFilePath));
        Assert.Equal(content, await File.ReadAllTextAsync(_testFilePath));
        Assert.Contains("Successfully created", result);
    }

    [Fact]
    public async Task WriteAsync_OverwritesFile_WhenFileExists()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "Old content");
        var content = "New content!";

        // Act
        var result = await _tool.WriteAsync(_testFilePath, content);

        // Assert
        Assert.Equal(content, await File.ReadAllTextAsync(_testFilePath));
        Assert.Contains("Successfully overwritten", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WriteAsync_ReturnsError_WhenFilePathIsNullOrWhiteSpace(string path)
    {
        // Act
        var result = await _tool.WriteAsync(path, "content");

        // Assert
        Assert.Equal("Error: File path cannot be null or empty.", result);
    }

    public void Dispose()
    {
        if (File.Exists(_testFilePath))
        {
            File.Delete(_testFilePath);
        }
    }
}
