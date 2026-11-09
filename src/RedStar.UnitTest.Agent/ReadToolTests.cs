using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RedStar.Agent.Tools;
using Xunit;

namespace RedStar.UnitTest.Agent;

public class ReadToolTests : IDisposable
{
    private readonly ReadTool _tool;
    private readonly string _testFilePath;

    public ReadToolTests()
    {
        _tool = new ReadTool(NullLogger<ReadTool>.Instance);
        _testFilePath = Path.Combine(Path.GetTempPath(), $"read_test_{Guid.NewGuid()}.txt");
    }

    [Fact]
    public async Task ReadAsync_ReturnsContent_WhenFileExists()
    {
        // Arrange
        var expectedContent = "Hello, Agent!";
        await File.WriteAllTextAsync(_testFilePath, expectedContent);

        // Act
        var result = await _tool.ReadAsync(_testFilePath);

        // Assert
        Assert.Equal(expectedContent, result);
    }

    [Fact]
    public async Task ReadAsync_ReturnsError_WhenFileDoesNotExist()
    {
        // Act
        var result = await _tool.ReadAsync(_testFilePath);

        // Assert
        Assert.StartsWith("Error: File '", result);
        Assert.EndsWith("' not found.", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReadAsync_ReturnsError_WhenFilePathIsNullOrWhiteSpace(string path)
    {
        // Act
        var result = await _tool.ReadAsync(path);

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
