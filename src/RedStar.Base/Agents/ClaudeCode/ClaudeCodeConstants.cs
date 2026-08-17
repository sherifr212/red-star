namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// Well-known values for <see cref="ClaudeCodeAgentOptions.AuthMode"/> -- how the spawned <c>claude</c>
/// process authenticates. A plain string rather than a closed enum, same rationale as
/// <see cref="AgentNames"/>: config binding/CLI flag parsing both work on a plain string with no converter.
/// </summary>
public static class ClaudeCodeAuthModes
{
    /// <summary>
    /// The spawned process inherits the parent environment as-is and authenticates via whatever
    /// <c>claude auth login</c> already set up on this machine (OAuth credentials in the system keychain).
    /// RedStar sets no <c>ANTHROPIC_API_KEY</c> and never passes <c>--bare</c> in this mode -- <c>--bare</c>
    /// explicitly skips keychain reads, which would break this mode's only auth path. Default.
    /// </summary>
    public const string CliLogin = "CliLogin";

    /// <summary>
    /// RedStar injects <see cref="ClaudeCodeAgentOptions.ApiKey"/> as the <c>ANTHROPIC_API_KEY</c>
    /// environment variable on the spawned process instead of relying on any locally logged-in credential.
    /// </summary>
    public const string ApiKey = "ApiKey";
}

/// <summary>
/// Well-known values for <see cref="ClaudeCodeAgentOptions.ProcessMode"/> -- how the <c>claude</c> CLI
/// subprocess is invoked across a <see cref="ChatSession"/>'s turns. A plain string, same rationale as
/// <see cref="ClaudeCodeAuthModes"/>.
/// </summary>
public static class ClaudeCodeProcessModes
{
    /// <summary>
    /// A fresh <c>claude -p</c> process is spawned for every turn (the first with <c>--session-id</c>, every
    /// later one with <c>--resume</c> against that same id) and allowed to exit once its <c>result</c> line
    /// arrives. Matches the stateless-call shape <see cref="Microsoft.Extensions.AI.IChatClient"/> already
    /// assumes, and leaves nothing to leak/kill beyond the in-flight call itself. Default.
    /// </summary>
    public const string PerTurn = "PerTurn";

    /// <summary>
    /// One <c>claude -p --input-format stream-json</c> process is spawned lazily on the first turn and kept
    /// alive for the rest of the <see cref="ChatSession"/>, fed one JSON line per turn over its open stdin
    /// pipe instead of being re-spawned. Avoids repeated process-startup latency, at the cost of a
    /// longer-lived subprocess that must be explicitly killed on cancellation/exit -- see
    /// <c>LongLivedClaudeCodeProcessRunner</c>.
    /// </summary>
    public const string LongLived = "LongLived";
}
