using Microsoft.Agents.AI;

using RedStar.Base.Agents.LMStudio;

namespace RedStar.UnitTest;

public class LMStudioAgentResponseExtractorTests
{
    private static readonly LMStudioAgentResponseExtractor Extractor = new();

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