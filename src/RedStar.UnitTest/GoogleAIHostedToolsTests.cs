using Microsoft.Extensions.AI;

using RedStar.Base.Agents.GoogleAI;

namespace RedStar.UnitTest;

public class GoogleAIHostedToolsTests
{
    [Fact]
    public void Known_IsMappedToolsThenNativeOnlyTools_WithNoDuplicates()
    {
        Assert.Equal(
            [.. GoogleAIHostedTools.MappedTools.Keys, .. GoogleAIHostedTools.NativeOnlyTools.Keys],
            GoogleAIHostedTools.Known);
        Assert.Equal(GoogleAIHostedTools.Known.Count, GoogleAIHostedTools.Known.Distinct().Count());
    }

    [Fact]
    public void MappedTools_ContainsGoogleSearchAndCodeExecution_ButNotUrlContext()
    {
        Assert.True(GoogleAIHostedTools.MappedTools.ContainsKey(GoogleAIHostedTools.GoogleSearch));
        Assert.True(GoogleAIHostedTools.MappedTools.ContainsKey(GoogleAIHostedTools.CodeExecution));
        Assert.False(GoogleAIHostedTools.MappedTools.ContainsKey(GoogleAIHostedTools.UrlContext));
    }

    [Fact]
    public void NativeOnlyTools_ContainsUrlContext_ButNotGoogleSearchOrCodeExecution()
    {
        Assert.True(GoogleAIHostedTools.NativeOnlyTools.ContainsKey(GoogleAIHostedTools.UrlContext));
        Assert.False(GoogleAIHostedTools.NativeOnlyTools.ContainsKey(GoogleAIHostedTools.GoogleSearch));
        Assert.False(GoogleAIHostedTools.NativeOnlyTools.ContainsKey(GoogleAIHostedTools.CodeExecution));
    }

    [Fact]
    public void MappedTools_LookupIsCaseInsensitive()
    {
        Assert.True(GoogleAIHostedTools.MappedTools.ContainsKey("googlesearch"));
        Assert.True(GoogleAIHostedTools.MappedTools.ContainsKey("GOOGLESEARCH"));
    }

    [Fact]
    public void NativeOnlyTools_LookupIsCaseInsensitive()
    {
        Assert.True(GoogleAIHostedTools.NativeOnlyTools.ContainsKey("urlcontext"));
        Assert.True(GoogleAIHostedTools.NativeOnlyTools.ContainsKey("URLCONTEXT"));
    }

    [Fact]
    public void MappedTools_GoogleSearch_ProducesHostedWebSearchTool()
    {
        var tool = GoogleAIHostedTools.MappedTools[GoogleAIHostedTools.GoogleSearch]();
        Assert.IsType<HostedWebSearchTool>(tool);
    }

    [Fact]
    public void MappedTools_CodeExecution_ProducesHostedCodeInterpreterTool()
    {
        var tool = GoogleAIHostedTools.MappedTools[GoogleAIHostedTools.CodeExecution]();
        Assert.IsType<HostedCodeInterpreterTool>(tool);
    }

    [Fact]
    public void NativeOnlyTools_UrlContext_ProducesToolWithUrlContextSet()
    {
        var tool = GoogleAIHostedTools.NativeOnlyTools[GoogleAIHostedTools.UrlContext]();
        Assert.NotNull(tool.UrlContext);
    }
}