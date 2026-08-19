using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

using RedStar.Base.Agents.ClaudeCode;

namespace RedStar.UnitTest;

public class ClaudeCodeAgentResponseExtractorTests
{
    private static readonly ClaudeCodeAgentResponseExtractor Extractor = new();

    /// <summary>Mirrors exactly what <see cref="ClaudeCodeChatClient"/> sets: the raw stream-json line as a
    /// plain string, one layer under <see cref="ChatResponseUpdate.RawRepresentation"/>, one more under
    /// <see cref="AgentResponseUpdate.RawRepresentation"/>.</summary>
    private static AgentResponseUpdate Wrap(string rawJsonLine) =>
        new() { RawRepresentation = new ChatResponseUpdate { RawRepresentation = rawJsonLine } };

    [Fact]
    public void TryGetToolStatus_ReturnsRunningLabel_ForToolUseStart()
    {
        var update = Wrap(
            """{"type":"stream_event","event":{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01","name":"Bash"}},"session_id":"x"}""");

        Assert.Equal("Running: Bash", Extractor.TryGetToolStatus(update));
    }

    [Fact]
    public void TryGetToolStatus_ReturnsNull_ForTextDelta()
    {
        var update = Wrap(
            """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"hi"}},"session_id":"x"}""");

        Assert.Null(Extractor.TryGetToolStatus(update));
    }

    [Fact]
    public void TryGetToolStatus_ReturnsNull_ForResultLine()
    {
        var update = Wrap("""{"type":"result","is_error":false,"session_id":"x"}""");

        Assert.Null(Extractor.TryGetToolStatus(update));
    }

    [Fact]
    public void TryGetToolStatus_ReturnsNull_WhenRawRepresentationIsNotAChatResponseUpdate()
    {
        var update = new AgentResponseUpdate { RawRepresentation = "not wrapped" };

        Assert.Null(Extractor.TryGetToolStatus(update));
    }

    [Fact]
    public void TryGetToolStatus_ReturnsNull_WhenInnerRawRepresentationIsNotAString()
    {
        var update = new AgentResponseUpdate { RawRepresentation = new ChatResponseUpdate { RawRepresentation = 123 } };

        Assert.Null(Extractor.TryGetToolStatus(update));
    }

    [Fact]
    public void TryGetToolStatus_ReturnsNull_ForNonClaudeCodeBackend() =>
        Assert.Null(Extractor.TryGetToolStatus(new AgentResponseUpdate()));

    [Fact]
    public void TryGetToolStatus_Throws_WhenUpdateIsNull() =>
        Assert.Throws<ArgumentNullException>(() => Extractor.TryGetToolStatus(null!));

    [Fact]
    public void TryGetWebSearchResults_AlwaysReturnsNull_EvenForToolUseWebSearch()
    {
        var update = Wrap(
            """{"type":"stream_event","event":{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01","name":"WebSearch"}},"session_id":"x"}""");

        Assert.Null(Extractor.TryGetWebSearchResults(update));
    }

    [Fact]
    public void TryGetWebSearchResults_Throws_WhenUpdateIsNull() =>
        Assert.Throws<ArgumentNullException>(() => Extractor.TryGetWebSearchResults(null!));
}