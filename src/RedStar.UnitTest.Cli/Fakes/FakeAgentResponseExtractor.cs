using Microsoft.Agents.AI;
using RedStar.Base;

namespace RedStar.UnitTest.Cli.Fakes;

internal sealed class FakeAgentResponseExtractor(
    Func<AgentResponseUpdate, string?>? toolStatus = null,
    Func<AgentResponseUpdate, int, IReadOnlyList<WebSearchResult>?>? webSearchResults = null) : IAgentResponseExtractor
{
    public string? TryGetToolStatus(AgentResponseUpdate update) => toolStatus?.Invoke(update);

    public IReadOnlyList<WebSearchResult>? TryGetWebSearchResults(AgentResponseUpdate update, int maxResults = 5) =>
        webSearchResults?.Invoke(update, maxResults);
}
