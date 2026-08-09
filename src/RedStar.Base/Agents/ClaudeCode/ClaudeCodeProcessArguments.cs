using System.Globalization;

namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// Builds the argument list shared by both <see cref="PerTurnClaudeCodeProcessRunner"/> and
/// <see cref="LongLivedClaudeCodeProcessRunner"/> -- everything except session continuity
/// (<c>--session-id</c>/<c>--resume</c>, PerTurn-only) and prompt delivery (a trailing positional argument
/// for PerTurn vs. <c>--input-format stream-json</c> stdin lines for LongLived), which differ enough between
/// the two runners that each builds its own remaining flags on top of <see cref="BuildCommonFlags"/>'s result.
/// </summary>
internal static class ClaudeCodeProcessArguments
{
    /// <summary>
    /// <c>--verbose</c> is not optional here: the live CLI (v2.1.224) rejects
    /// <c>--print --output-format=stream-json</c> with "requires --verbose" if it's omitted --
    /// confirmed against the real binary, not just documentation. <c>--include-partial-messages</c> is what
    /// makes token-level <c>content_block_delta</c> events show up at all (without it, only one full
    /// <c>assistant</c> message per turn is emitted, which would make RedStar's answer box appear all at
    /// once instead of streaming) -- see <see cref="ClaudeCodeStreamJsonParser"/>.
    /// </summary>
    public static List<string> BuildCommonFlags(ClaudeCodeAgentOptions options, string modelId)
    {
        ArgumentNullException.ThrowIfNull(options);

        var arguments = new List<string>
        {
            "--print",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
        };

        if (!string.IsNullOrEmpty(modelId))
        {
            arguments.Add("--model");
            arguments.Add(modelId);
        }

        if (options.Bare)
        {
            arguments.Add("--bare");
        }

        if (!string.IsNullOrEmpty(options.PermissionMode))
        {
            arguments.Add("--permission-mode");
            arguments.Add(options.PermissionMode);
        }

        // Always passed, even when empty (as a lone "" argument -- verified against the real CLI, which
        // accepts it as "zero allowed tools") rather than omitted: omitting --allowedTools entirely still
        // advertises every built-in tool to the model (it just gets denied at execution time with no
        // human to prompt), which wastes a turn on a doomed tool call instead of the model never
        // considering one. Explicitly locking it down enforces ClaudeCodeAgentOptions.AllowedTools's
        // "opt-in, nothing by default" default precisely rather than only implying it.
        arguments.Add("--allowedTools");
        if (options.AllowedTools.Count > 0)
        {
            arguments.AddRange(options.AllowedTools);
        }
        else
        {
            arguments.Add("");
        }

        if (options.DisallowedTools.Count > 0)
        {
            arguments.Add("--disallowedTools");
            arguments.AddRange(options.DisallowedTools);
        }

        if (options.MaxBudgetUsd is { } maxBudgetUsd)
        {
            arguments.Add("--max-budget-usd");
            arguments.Add(maxBudgetUsd.ToString(CultureInfo.InvariantCulture));
        }

        return arguments;
    }
}
