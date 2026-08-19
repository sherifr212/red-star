using Microsoft.Agents.AI;

using RedStar.Base.Agents.GoogleAI;

namespace RedStar.UnitTest;

public class GoogleAIAgentResponseExtractorTests
{
    private static readonly GoogleAIAgentResponseExtractor Extractor = new();

    [Fact]
    public void TryGetToolStatus_AlwaysReturnsNull() =>
        Assert.Null(Extractor.TryGetToolStatus(new AgentResponseUpdate()));

    [Fact]
    public void TryGetWebSearchResults_AlwaysReturnsNull() =>
        Assert.Null(Extractor.TryGetWebSearchResults(new AgentResponseUpdate()));

    [Fact]
    public void TryGetToolStatus_Throws_WhenUpdateIsNull() =>
        Assert.Throws<ArgumentNullException>(() => Extractor.TryGetToolStatus(null!));

    [Fact]
    public void TryGetWebSearchResults_Throws_WhenUpdateIsNull() =>
        Assert.Throws<ArgumentNullException>(() => Extractor.TryGetWebSearchResults(null!));
}