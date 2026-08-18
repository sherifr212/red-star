using Microsoft.Extensions.AI;
using GenAITool = Google.GenAI.Types.Tool;
using GenAIUrlContext = Google.GenAI.Types.UrlContext;

namespace RedStar.Base.Agents.GoogleAI;

/// <summary>
/// Names of Gemini's built-in, server-side "hosted" tools -- executed entirely by Google's own
/// infrastructure, unlike the client-side <see cref="AIFunction"/>s passed through
/// <see cref="GoogleAIAgentFactory.Create"/>'s <c>tools</c> parameter (those round-trip a
/// <see cref="FunctionCallContent"/>/<see cref="FunctionResultContent"/> back through RedStar; a
/// hosted tool never does -- its results are already folded into the response). These are the keys of
/// <see cref="GoogleAIAgentOptions.HostedTools"/>; see <see cref="MappedTools"/>/
/// <see cref="NativeOnlyTools"/> and <see cref="GoogleAIAgentFactory.CreateChatOptions"/> for how each
/// is mapped onto the request.
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
    /// Hosted tools with a native <c>Microsoft.Extensions.AI</c> <see cref="AITool"/> marker the
    /// <c>Google.GenAI</c> SDK's <c>IChatClient</c> already knows how to translate into Gemini's native
    /// tool entry -- <see cref="GoogleAIAgentFactory.CreateChatOptions"/> adds these straight onto
    /// <see cref="ChatOptions.Tools"/> alongside any client-injected tool. Keyed
    /// case-insensitively (<see cref="StringComparer.OrdinalIgnoreCase"/>), matching
    /// <see cref="GoogleAIAgentOptions.HostedTools"/>. Adding a newly-modeled hosted tool here is a
    /// one-line addition, not a new branch in <see cref="GoogleAIAgentFactory.CreateChatOptions"/>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<AITool>> MappedTools =
        new Dictionary<string, Func<AITool>>(StringComparer.OrdinalIgnoreCase)
        {
            [GoogleSearch] = static () => new HostedWebSearchTool(),
            [CodeExecution] = static () => new HostedCodeInterpreterTool(),
        };

    /// <summary>
    /// Hosted tools with no <c>Microsoft.Extensions.AI</c>-modeled equivalent, so they can't go through
    /// <see cref="ChatOptions.Tools"/> -- <see cref="GoogleAIAgentFactory.CreateChatOptions"/> instead
    /// adds these as raw <c>Google.GenAI.Types.Tool</c> entries via
    /// <see cref="ChatOptions.RawRepresentationFactory"/>. Same case-insensitive keying as
    /// <see cref="MappedTools"/>. Extending to a future Gemini-native tool with no
    /// <c>Microsoft.Extensions.AI</c> equivalent (e.g. Google Maps grounding) is a one-line addition
    /// here, not a new branch or a second <see cref="ChatOptions.RawRepresentationFactory"/> owner --
    /// see that method's remarks for why every native-only tool must accumulate into this single list
    /// rather than each claiming the factory slot for itself.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<GenAITool>> NativeOnlyTools =
        new Dictionary<string, Func<GenAITool>>(StringComparer.OrdinalIgnoreCase)
        {
            [UrlContext] = static () => new GenAITool { UrlContext = new GenAIUrlContext() },
        };

    /// <summary>
    /// Every known hosted tool name, in <see cref="MappedTools"/> order followed by
    /// <see cref="NativeOnlyTools"/> order -- used to build
    /// <see cref="GoogleAIAgentOptions.HostedTools"/>'s default dictionary so the checked-in config
    /// template always lists every available hosted tool (each off by default) rather than requiring
    /// users to already know the exact key name to opt in. Derived from the two mapping tables rather
    /// than listed separately, so there is exactly one place a new hosted tool needs to be registered.
    /// </summary>
    public static readonly IReadOnlyList<string> Known = [.. MappedTools.Keys, .. NativeOnlyTools.Keys];
}
