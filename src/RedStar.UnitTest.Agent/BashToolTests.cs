using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using RedStar.Agent.Tools;
using Xunit;

namespace RedStar.UnitTest.Agent;

public class BashToolTests
{
    private readonly BashTool _tool;

    public BashToolTests()
    {
        _tool = new BashTool(NullLogger<BashTool>.Instance);
    }

    [Fact]
    public async Task RunBashAsync_ExecutesCommand_AndReturnsOutput()
    {
        // Act
        var result = await _tool.RunBashAsync("echo 'Hello from bash'");

        // Assert
        Assert.Contains("Hello from bash", result);
    }

    [Fact]
    public async Task RunBashAsync_ReturnsError_WhenCommandFails()
    {
        // Act
        var result = await _tool.RunBashAsync("ls /non/existent/path/for/test");

        // Assert
        Assert.Contains("Command exited with code", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunBashAsync_ReturnsError_WhenCommandIsNullOrWhiteSpace(string command)
    {
        // Act
        var result = await _tool.RunBashAsync(command);

        // Assert
        Assert.Equal("Error: Command cannot be null or empty.", result);
    }
}
