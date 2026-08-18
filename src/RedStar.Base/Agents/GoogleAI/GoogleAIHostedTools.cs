namespace RedStar.Base.Agents.GoogleAI;

/// <summary>
/// Names of Gemini's built-in, server-side "hosted" tools -- executed entirely by Google's own
/// infrastructure, unlike the client-side <see cref="Microsoft.Extensions.AI.AIFunction"/>s passed
/// through <see cref="GoogleAIAgentFactory.Create"/>'s <c>tools</c> parameter (those round-trip a
/// <see cref="Microsoft.Extensions.AI.FunctionCallContent"/>/
/// <see cref="Microsoft.Extensions.AI.FunctionResultContent"/> back through RedStar; a hosted tool
/// never does -- its results are already folded into the response). These are the keys of
/// <see cref="GoogleAIAgentOptions.HostedTools"/>; see
/// <see cref="GoogleAIAgentFactory.CreateChatOptions"/> for how each is mapped onto the request.
/// </summary>
public static class GoogleAIHostedTools
{
    /// <summary>Grounds responses in live Google Search results.</summary>
    public const string GoogleSearch = "GoogleSearch";

    /// <summary>
    /// Lets the model write and run Python to help generate a response. Runs entirely in Google's own
    /// sandboxed execution environment -- never on the machine running RedStar -- so "only run it if
    /// safe" is a guarantee Google's infrastructure makes, not something RedStar enforces itself.
    /// </summary>
    public const string CodeExecution = "CodeExecution";

    /// <summary>Lets the model fetch and read the content of URLs it's given or finds.</summary>
    public const string UrlContext = "UrlContext";

    /// <summary>
    /// Every known hosted tool name, in the same order <see cref="GoogleAIAgentOptions.HostedTools"/>
    /// is pre-populated with -- used to build that default dictionary so the checked-in config
    /// template always lists every available hosted tool (each off by default) rather than requiring
    /// users to already know the exact key name to opt in.
    /// </summary>
    public static readonly IReadOnlyList<string> Known = [GoogleSearch, CodeExecution, UrlContext];
}
