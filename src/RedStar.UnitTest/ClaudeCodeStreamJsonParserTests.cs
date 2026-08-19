using RedStar.Base.Agents.ClaudeCode;

namespace RedStar.UnitTest;

/// <summary>
/// Fixture lines marked "captured" below are real <c>claude -p --output-format stream-json</c> output from a
/// live run against the actual CLI (v2.1.224), not hand-authored guesses at the protocol shape -- pulled
/// verbatim from a manual verification pass while designing <see cref="ClaudeCodeStreamJsonParser"/>. Lines
/// marked "synthetic" follow the standard Anthropic Messages API content-block shape (stable/documented
/// independent of the CLI) for cases not exercised by that pass, e.g. a <c>tool_use</c> block (every
/// verification run had tools disabled via <c>--allowedTools ""</c>).
/// </summary>
public class ClaudeCodeStreamJsonParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseLine_ReturnsNull_ForBlankInput(string? blank) =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(blank!));

    [Fact]
    public void TryParseLine_ReturnsNull_ForMalformedJson() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine("{not json"));

    [Fact]
    public void TryParseLine_ReturnsNull_ForJsonArray_NotObject() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine("[1,2,3]"));

    [Fact]
    public void TryParseLine_ReturnsNull_ForObjectWithNoTypeProperty() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine("""{"foo":"bar"}"""));

    // captured
    [Fact]
    public void TryParseLine_ReturnsNull_ForSystemInitLine() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"system","subtype":"init","cwd":"C:\\repo","session_id":"22222222-2222-2222-2222-222222222222","tools":["Bash","Read"],"mcp_servers":[],"model":"claude-sonnet-5","permissionMode":"default","apiKeySource":"none","claude_code_version":"2.1.224"}"""));

    // captured
    [Fact]
    public void TryParseLine_ReturnsNull_ForRateLimitEventLine() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":1786320600,"rateLimitType":"five_hour"},"uuid":"1598a469","session_id":"22222222-2222-2222-2222-222222222222"}"""));

    // captured
    [Fact]
    public void TryParseLine_ReturnsNull_ForAssistantLine() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"assistant","message":{"model":"claude-sonnet-5","id":"msg_011","type":"message","role":"assistant","content":[{"type":"text","text":"1\n2\n3\n4\n5"}]},"session_id":"22222222-2222-2222-2222-222222222222"}"""));

    // captured
    [Fact]
    public void TryParseLine_ReturnsNull_ForMessageStartStreamEvent() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"stream_event","event":{"type":"message_start","message":{"model":"claude-sonnet-5","id":"msg_011","type":"message","role":"assistant","content":[]}},"session_id":"x"}"""));

    // captured
    [Fact]
    public void TryParseLine_ReturnsNull_ForContentBlockStop() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"stream_event","event":{"type":"content_block_stop","index":0},"session_id":"x"}"""));

    // captured
    [Fact]
    public void TryParseLine_ReturnsNull_ForMessageDelta() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"stream_event","event":{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":11}},"session_id":"x"}"""));

    // captured
    [Fact]
    public void TryParseLine_ReturnsNull_ForMessageStop() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"stream_event","event":{"type":"message_stop"},"session_id":"x"}"""));

    // captured -- a content_block_start for plain text (not tool_use) has no "name" field to extract anyway
    [Fact]
    public void TryParseLine_ReturnsNull_ForContentBlockStart_TextBlock() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"stream_event","event":{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}},"session_id":"x"}"""));

    // captured
    [Fact]
    public void TryParseLine_ReturnsTextDelta_ForContentBlockDelta_TextDelta()
    {
        var line = """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"1"}},"session_id":"x"}""";

        var parsed = ClaudeCodeStreamJsonParser.TryParseLine(line);

        Assert.NotNull(parsed);
        Assert.Equal("1", parsed.Value.TextDelta);
        Assert.Null(parsed.Value.ToolUseName);
        Assert.Null(parsed.Value.Result);
        Assert.Equal(line, parsed.Value.RawJson);
    }

    [Fact]
    public void TryParseLine_ReturnsNull_ForContentBlockDelta_WithEmptyText() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":""}},"session_id":"x"}"""));

    // synthetic -- input_json_delta streams a tool call's arguments, not answer text
    [Fact]
    public void TryParseLine_ReturnsNull_ForContentBlockDelta_InputJsonDelta() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"stream_event","event":{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"comm"}},"session_id":"x"}"""));

    // synthetic -- standard Anthropic Messages API tool_use content-block shape
    [Fact]
    public void TryParseLine_ReturnsToolUseName_ForContentBlockStart_ToolUse()
    {
        var line = """{"type":"stream_event","event":{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01","name":"Bash","input":{}}},"session_id":"x"}""";

        var parsed = ClaudeCodeStreamJsonParser.TryParseLine(line);

        Assert.NotNull(parsed);
        Assert.Equal("Bash", parsed.Value.ToolUseName);
        Assert.Null(parsed.Value.TextDelta);
        Assert.Null(parsed.Value.Result);
    }

    // synthetic -- a tool_use block with a blank name is not a realistic shape, but must not crash/return one
    [Fact]
    public void TryParseLine_ReturnsNull_ForContentBlockStart_ToolUse_WithEmptyName() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine(
            """{"type":"stream_event","event":{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_01","name":""}},"session_id":"x"}"""));

    [Fact]
    public void TryParseLine_ReturnsNull_ForStreamEvent_WithNoEventProperty() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine("""{"type":"stream_event"}"""));

    // captured -- a real successful `claude -p ... --output-format json` result (v2.1.224)
    [Fact]
    public void TryParseLine_ParsesResult_Success()
    {
        var line = """
            {"is_error":false,"duration_api_ms":2252,"num_turns":1,"stop_reason":"end_turn","session_id":"11111111-1111-1111-1111-111111111111","total_cost_usd":0.131763,"usage":{"input_tokens":2,"cache_creation_input_tokens":20894,"cache_read_input_tokens":21110,"output_tokens":4,"server_tool_use":{"web_search_requests":0,"web_fetch_requests":0},"service_tier":"standard"},"modelUsage":{"claude-sonnet-5":{"inputTokens":2,"outputTokens":4,"costUSD":0.131763}},"permission_denials":[],"terminal_reason":"completed","subtype":"success","api_error_status":null,"result":"hello","ttft_ms":2362,"type":"result","duration_ms":2427,"uuid":"f8325b57-8a25-433f-a6db-c0ffc27ed8c7"}
            """.Trim();

        var parsed = ClaudeCodeStreamJsonParser.TryParseLine(line);

        Assert.NotNull(parsed);
        Assert.Null(parsed.Value.TextDelta);
        Assert.Null(parsed.Value.ToolUseName);
        var result = parsed.Value.Result;
        Assert.NotNull(result);
        Assert.Equal("11111111-1111-1111-1111-111111111111", result!.SessionId);
        Assert.False(result.IsError);
        Assert.Equal("success", result.Subtype);
        Assert.Equal(0.131763, result.TotalCostUsd);
        Assert.Equal(4, result.OutputTokens);
        Assert.Equal("hello", result.ResultText);
        Assert.Equal(line, parsed.Value.RawJson);
    }

    // synthetic error shape, following the same field names as the captured success line
    [Fact]
    public void TryParseLine_ParsesResult_Error()
    {
        var line = """{"type":"result","is_error":true,"subtype":"error_during_execution","session_id":"s1","result":"something failed"}""";

        var result = ClaudeCodeStreamJsonParser.TryParseLine(line)!.Value.Result;

        Assert.NotNull(result);
        Assert.True(result!.IsError);
        Assert.Equal("error_during_execution", result.Subtype);
        Assert.Equal("something failed", result.ResultText);
    }

    [Fact]
    public void TryParseLine_ParsesResult_WithMissingOptionalFields_AsNulls()
    {
        var result = ClaudeCodeStreamJsonParser.TryParseLine("""{"type":"result"}""")!.Value.Result;

        Assert.NotNull(result);
        Assert.Null(result!.SessionId);
        Assert.False(result.IsError);
        Assert.Null(result.Subtype);
        Assert.Null(result.TotalCostUsd);
        Assert.Null(result.OutputTokens);
        Assert.Null(result.ResultText);
    }

    [Fact]
    public void TryParseLine_ParsesResult_WithNonObjectUsage_AsNullOutputTokens() =>
        Assert.Null(ClaudeCodeStreamJsonParser.TryParseLine("""{"type":"result","usage":"not-an-object"}""")!.Value.Result!.OutputTokens);
}