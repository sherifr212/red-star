namespace RedStar.Base.Agents.Unsloth;

/// <summary>
/// Names Unsloth's <c>enabled_tools</c> field documents
/// (https://unsloth.ai/docs/integrations/connect-curl-and-http-to-unsloth): server-side Python execution,
/// Bash execution, and web search. <see cref="UnslothAgentOptions.EnabledTools"/> itself is free-form --
/// any name the server recognizes works via config alone without a code change here -- <see cref="Known"/>
/// only drives which rows <c>RedStar.Cli.ChatCommandHandler.PrintStartupInfoBox</c> lists by default so a
/// run's startup box always shows every documented tool's on/off state, not just the ones a user happened
/// to enable.
/// </summary>
public static class UnslothTools
{
    public const string Python = "python";
    public const string Bash = "bash";
    public const string WebSearch = "web_search";

    public static readonly IReadOnlyList<string> Known = [Python, Bash, WebSearch];
}