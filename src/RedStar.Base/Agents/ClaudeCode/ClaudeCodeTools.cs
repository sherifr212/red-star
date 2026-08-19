namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// The <c>claude</c> CLI's built-in tool names, as reported by a real session's
/// <c>{"type":"system","subtype":"init",...,"tools":[...]}</c> line (v2.1.224) -- these are the values
/// <see cref="ClaudeCodeAgentOptions.AllowedTools"/>/<see cref="ClaudeCodeAgentOptions.DisallowedTools"/>
/// accept (plus scoped forms like <c>"Bash(git log *)"</c>, which is why those fields stay free-form lists
/// rather than being validated against this catalog). <see cref="Known"/> only drives which rows
/// <c>RedStar.Cli.ChatCommandHandler.PrintStartupInfoBox</c> lists by default, same purpose as
/// <see cref="RedStar.Base.Agents.Unsloth.UnslothTools.Known"/> -- so a run's startup box always shows every
/// common tool's on/off state, not just the ones a user happened to enable. The live session also reports
/// several RedStar/Claude-Code-tooling-specific entries (e.g. <c>Task</c>, <c>ToolSearch</c>, <c>Skill</c>)
/// omitted here as noise for a startup summary -- <see cref="ClaudeCodeAgentOptions.AllowedTools"/> can still
/// name any of them; this list is a curated "most commonly toggled" set, not the CLI's full inventory.
/// </summary>
public static class ClaudeCodeTools
{
    public const string Read = "Read";
    public const string Grep = "Grep";
    public const string Glob = "Glob";
    public const string Bash = "Bash";
    public const string Edit = "Edit";
    public const string Write = "Write";
    public const string WebSearch = "WebSearch";
    public const string WebFetch = "WebFetch";

    public static readonly IReadOnlyList<string> Known = [Read, Grep, Glob, Bash, Edit, Write, WebSearch, WebFetch];
}