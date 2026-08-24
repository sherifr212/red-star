namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// Drives the <c>claude</c> CLI subprocess for one <see cref="RedStarChatSession"/>'s worth of turns. Session
/// continuity and process lifecycle are entirely internal to the implementation -- see
/// <see cref="PerTurnClaudeCodeProcessRunner"/> (spawns fresh every turn, the
/// <see cref="ClaudeCodeAgentOptions.ProcessMode"/> default) and <see cref="LongLivedClaudeCodeProcessRunner"/>
/// (one process kept alive across every turn), selected by <c>ClaudeCodeAgentFactory.Create</c>.
/// </summary>
public interface IClaudeCodeProcessRunner : IAsyncDisposable
{
    /// <summary>
    /// Sends one turn and streams back the raw stream-json lines produced for it (see
    /// <see cref="ClaudeCodeStreamJsonParser"/>), ending once that turn's <c>result</c> line has been
    /// yielded. <paramref name="instructions"/> (the system prompt) is only ever actually used on this
    /// runner's first call: Claude Code's system prompt is established once per session, so re-sending it on
    /// a resumed/continuing turn would be redundant at best.
    /// </summary>
    IAsyncEnumerable<string> SendAsync(string userText, string? instructions, CancellationToken cancellationToken);
}