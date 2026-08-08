using System.ClientModel.Primitives;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using RedStar.Base;
using RedStar.Base.Agents.Unsloth;

namespace RedStar.UnitTest;

public class UnslothAgentResponseExtractorTests
{
    private static readonly UnslothAgentResponseExtractor Extractor = new();

    private static AgentResponseUpdate Wrap(string json)
    {
        var streamingUpdate = ModelReaderWriter.Read<StreamingChatCompletionUpdate>(
            BinaryData.FromString(json), ModelReaderWriterOptions.Json)!;
        var chatResponseUpdate = new ChatResponseUpdate { RawRepresentation = streamingUpdate };
        return new AgentResponseUpdate { RawRepresentation = chatResponseUpdate };
    }

    [Fact]
    public void TryGetToolStatus_ReturnsContent_ForToolStatusEvent()
    {
        var update = Wrap("""
            {"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[],
             "type":"tool_status","content":"Searching: current year"}
            """);

        Assert.Equal("Searching: current year", Extractor.TryGetToolStatus(update));
    }

    [Fact]
    public void TryGetToolStatus_ReturnsNull_ForOrdinaryChunk()
    {
        var update = Wrap("""{"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[]}""");

        Assert.Null(Extractor.TryGetToolStatus(update));
    }

    [Fact]
    public void TryGetToolStatus_ReturnsNull_ForDifferentEventType()
    {
        var update = Wrap("""
            {"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[],
             "type":"tool_end","tool_name":"web_search","result":"whatever"}
            """);

        Assert.Null(Extractor.TryGetToolStatus(update));
    }

    [Fact]
    public void TryGetToolStatus_ReturnsNull_WhenContentIsEmpty()
    {
        var update = Wrap("""
            {"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[],
             "type":"tool_status","content":""}
            """);

        Assert.Null(Extractor.TryGetToolStatus(update));
    }

    [Fact]
    public void TryGetToolStatus_ReturnsNull_ForNonOpenAiBackend()
    {
        var update = new AgentResponseUpdate();

        Assert.Null(Extractor.TryGetToolStatus(update));
    }

    [Fact]
    public void TryGetWebSearchResults_ParsesTitleAndUrlBlocks_FromToolEndResult()
    {
        var resultText =
            "Title: Example One\nURL: https://example.com/one\nSnippet: one\n\n---\n\n" +
            "Title: Example Two\nURL: https://example.com/two\nSnippet: two\n\n---\n\n" +
            "Some instructional trailer with neither field.";

        var update = Wrap($$"""
            {"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[],
             "type":"tool_end","tool_name":"web_search","result":{{System.Text.Json.JsonSerializer.Serialize(resultText)}}}
            """);

        var results = Extractor.TryGetWebSearchResults(update);

        Assert.NotNull(results);
        Assert.Equal(
            [new WebSearchResult("Example One", "https://example.com/one"), new WebSearchResult("Example Two", "https://example.com/two")],
            results);
    }

    [Fact]
    public void TryGetWebSearchResults_RespectsMaxResults()
    {
        var resultText = string.Join(
            "\n\n---\n\n",
            Enumerable.Range(1, 10).Select(i => $"Title: Result {i}\nURL: https://example.com/{i}"));

        var update = Wrap($$"""
            {"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[],
             "type":"tool_end","tool_name":"web_search","result":{{System.Text.Json.JsonSerializer.Serialize(resultText)}}}
            """);

        var results = Extractor.TryGetWebSearchResults(update, maxResults: 3);

        Assert.NotNull(results);
        Assert.Equal(3, results!.Count);
    }

    [Fact]
    public void TryGetWebSearchResults_ReturnsNull_ForPageFetchResult_WithNoTitleUrlLines()
    {
        var update = Wrap($$"""
            {"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[],
             "type":"tool_end","tool_name":"web_search","result":"Just raw page content, no Title/URL lines."}
            """);

        Assert.Null(Extractor.TryGetWebSearchResults(update));
    }

    [Fact]
    public void TryGetWebSearchResults_ReturnsNull_ForDifferentEventType()
    {
        var update = Wrap("""
            {"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[],
             "type":"tool_status","content":"Searching: x"}
            """);

        Assert.Null(Extractor.TryGetWebSearchResults(update));
    }

    [Fact]
    public void TryGetWebSearchResults_ReturnsNull_WhenToolNameIsNotWebSearch()
    {
        var update = Wrap($$"""
            {"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[],
             "type":"tool_end","tool_name":"read_page","result":"Title: X\nURL: https://example.com"}
            """);

        Assert.Null(Extractor.TryGetWebSearchResults(update));
    }

    [Fact]
    public void TryGetWebSearchResults_ReturnsNull_WhenResultPropertyIsMissing()
    {
        var update = Wrap("""
            {"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[],
             "type":"tool_end","tool_name":"web_search"}
            """);

        Assert.Null(Extractor.TryGetWebSearchResults(update));
    }

    [Fact]
    public void TryGetWebSearchResults_ReturnsNull_WhenResultIsEmpty()
    {
        var update = Wrap("""
            {"id":"1","object":"chat.completion.chunk","created":0,"model":"m","choices":[],
             "type":"tool_end","tool_name":"web_search","result":""}
            """);

        Assert.Null(Extractor.TryGetWebSearchResults(update));
    }

    [Fact]
    public void TryGetWebSearchResults_ReturnsNull_ForNonOpenAiBackend()
    {
        var update = new AgentResponseUpdate();

        Assert.Null(Extractor.TryGetWebSearchResults(update));
    }

    [Fact]
    public void TryGetToolStatus_Throws_WhenUpdateIsNull() =>
        Assert.Throws<ArgumentNullException>(() => Extractor.TryGetToolStatus(null!));

    [Fact]
    public void TryGetWebSearchResults_Throws_WhenUpdateIsNull() =>
        Assert.Throws<ArgumentNullException>(() => Extractor.TryGetWebSearchResults(null!));
}
