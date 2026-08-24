using Microsoft.Agents.AI;

namespace RedStar.Base;

/// <summary>
/// Extracts provider-specific side-channel data (tool-activity labels, search results, ...) from a
/// streamed <see cref="AgentResponseUpdate"/>. Kept agent-agnostic like <see cref="RedStarChatSession"/> and
/// <see cref="RedStarOptions"/> -- callers such as <c>ChatCommandHandler</c> depend on this interface
/// rather than on a concrete agent's factory, so a future second agent under
/// <c>RedStar.Base/Agents/&lt;AgentName&gt;</c> can plug in its own implementation without CLI-side
/// branching on which agent is active. See <c>RedStar.Base.Agents.Unsloth.UnslothAgentResponseExtractor</c>
/// for the only implementation today.
/// </summary>
public interface IAgentResponseExtractor
{
    /// <summary>Human-readable label for server-side tool activity (e.g. "Searching: current year"), if
    /// <paramref name="update"/> carries one. Returns null when it doesn't, including for backends that
    /// don't emit this kind of event at all.</summary>
    string? TryGetToolStatus(AgentResponseUpdate update);

    /// <summary>Completed search-hit list (title + URL), if <paramref name="update"/> is the terminal event
    /// for a search-style tool call. Returns null when it isn't, including for backends that don't emit
    /// this kind of event at all.</summary>
    IReadOnlyList<WebSearchResult>? TryGetWebSearchResults(AgentResponseUpdate update);
}

/// <summary>One hit from a server-side web-search tool call.</summary>
public sealed record WebSearchResult(string Title, string Url);