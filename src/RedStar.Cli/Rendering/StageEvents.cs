using System.Collections.Generic;

namespace RedStar.Cli.Rendering;

/// <summary>
/// Well-known stage identifiers for one leg of a turn's lifecycle. A stage is a plain string, not a
/// closed C# enum: <see cref="StageEvent"/>, <see cref="StageBox"/>, and the <c>redstar.stage.duration</c>
/// telemetry tag all carry whatever string a producer hands them, so a future producer (a different
/// agent under <c>RedStar.Base/Agents/&lt;AgentName&gt;</c>, or a new kind of Unsloth server event) can
/// introduce its own stage label without a shared enum in this file having to grow a member for it first.
/// <see cref="Other"/> is the initial "nothing has happened yet" box every turn opens with, and doubles
/// as the fallback for any stage <see cref="StageBox.StageStyle"/> doesn't have specific styling for; it
/// never appears for a reason a user would otherwise see labeled, which is why it gets the
/// deliberately-odd grey rather than one of the other three's saturated colors.
/// </summary>
internal static class TurnStage
{
    public const string Other = "Other";
    public const string Reasoning = "Reasoning";
    public const string Searching = "Searching";
    public const string Generating = "Generating";
}

/// <summary>One piece of one stage's content: a text delta to append, a completed site list, or a
/// final output-token count.
/// </summary>
internal readonly record struct StageEvent(
    string Stage, string? TextDelta, IReadOnlyList<RedStar.Base.WebSearchResult>? Sites, int? OutputTokenCount = null);

/// <summary>Result of draining one stage's events until either a differently-staged event arrives
/// (<see cref="NextEvent"/> set, <see cref="SplitForHeight"/> false) or the current box's estimated
/// height crossed the safe-to-redraw threshold before the stage itself changed (<see cref="NextEvent"/>
/// null, <see cref="SplitForHeight"/> true) -- the latter tells the caller to seal the current box and
/// open a same-stage continuation instead of waiting for a real stage transition.
/// </summary>
internal readonly record struct DrainResult(StageEvent? NextEvent, bool SplitForHeight);
