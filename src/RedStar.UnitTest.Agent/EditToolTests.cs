using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RedStar.Agent.Tools;
using Xunit;

namespace RedStar.UnitTest.Agent;

public class EditToolTests : IDisposable
{
    private readonly EditTool _tool;
    private readonly string _testFilePath;

    public EditToolTests()
    {
        _tool = new EditTool(NullLogger<EditTool>.Instance);
        _testFilePath = Path.Combine(Path.GetTempPath(), $"edit_test_{Guid.NewGuid()}.txt");
    }

    [Fact]
    public async Task EditAsync_ModifiesFile_WhenSearchStringFound()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "Hello, World!");

        // Act
        var result = await _tool.EditAsync(_testFilePath, "World", "Agent");

        // Assert
        Assert.Equal("Hello, Agent!", await File.ReadAllTextAsync(_testFilePath));
        Assert.Contains("Successfully edited", result);
    }

    [Fact]
    public async Task EditAsync_ReturnsError_WhenSearchStringNotFound()
    {
        // Arrange
        await File.WriteAllTextAsync(_testFilePath, "Hello, World!");

        // Act
        var result = await _tool.EditAsync(_testFilePath, "Agent", "Universe");

        // Assert
        Assert.Contains("Search string not found", result);
        Assert.Equal("Hello, World!", await File.ReadAllTextAsync(_testFilePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EditAsync_ReturnsError_WhenFilePathIsNullOrWhiteSpace(string path)
    {
        // Act
        var result = await _tool.EditAsync(path, "search", "replace");

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
