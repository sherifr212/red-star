namespace RedStar.Base;

/// <summary>
/// Well-known values for <see cref="RedStarOptions.Agent"/> -- which agent backend a run talks to.
/// A plain string rather than a closed enum, matching the same rationale as
/// <c>RedStar.Cli.ChatCommandHandler.TurnStage</c>: a future third agent under
/// <c>RedStar.Base/Agents/&lt;AgentName&gt;</c> doesn't need a shared enum in this file to grow a member
/// for it first, and config binding/CLI flag parsing both work on a plain string with no converter.
/// </summary>
public static class AgentNames
{
    public const string Unsloth = "Unsloth";
    public const string LMStudio = "LMStudio";
    public const string ClaudeCode = "ClaudeCode";
}
