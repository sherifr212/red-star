using System;
using System.Diagnostics;
using System.IO;

using RedStar.Cli.Rendering;

using Spectre.Console;

namespace RedStar.UnitTest.Cli;

public class StageBoxTests
{
    private static string RenderToPlainText(StageBox box)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Write(box.Render(final: true));
        return writer.ToString();
    }

    [Fact]
    public void Render_ShowsTokensAndSpeed_OnAFirstBox_WhenUsageArrived()
    {
        var box = new StageBox(TurnStage.Generating, Stopwatch.StartNew());
        box.Apply(new StageEvent(TurnStage.Generating, "hi", null));
        box.Apply(new StageEvent(TurnStage.Generating, null, null, 42));

        Assert.Contains("42 tok", RenderToPlainText(box));
    }

    /// <summary>
    /// Regression test: the trailing <c>UsageContent</c> update is the last event of the whole turn, so
    /// for any response long enough to trip a height-based split it lands on a same-stage "(cont'd)"
    /// continuation box, not the stage's first box -- see the remarks on
    /// <see cref="StageBox"/>'s <c>TokensAndSpeedLabel</c>. The footer used to unconditionally hide the
    /// token/speed label on a continuation box, which meant it silently never rendered anywhere for a
    /// split turn even though <see cref="StageBox.OutputTokenCount"/> had the value.
    /// </summary>
    [Fact]
    public void Render_ShowsTokensAndSpeed_OnAContinuationBox_WhenUsageArrived()
    {
        var box = new StageBox(TurnStage.Generating, Stopwatch.StartNew(), isContinuation: true);
        box.Apply(new StageEvent(TurnStage.Generating, "more text", null));
        box.Apply(new StageEvent(TurnStage.Generating, null, null, 42));

        Assert.Contains("42 tok", RenderToPlainText(box));
    }

    [Fact]
    public void Render_OmitsTokensAndSpeed_WhenNoUsageArrived()
    {
        var box = new StageBox(TurnStage.Generating, Stopwatch.StartNew());
        box.Apply(new StageEvent(TurnStage.Generating, "hi", null));

        Assert.DoesNotContain("tok,", RenderToPlainText(box));
    }

    /// <summary>
    /// Regression test: a turn with two back-to-back searches used to bucket every status label into one
    /// block and every hit from both calls into a single trailing block, which visually merged the second
    /// query's results under the first query's label (or vice versa). Each search's status and its own
    /// hits must render in the order they actually happened.
    /// </summary>
    [Fact]
    public void Render_InterleavesSequentialSearches_InOrder()
    {
        var box = new StageBox(TurnStage.Searching, Stopwatch.StartNew());
        box.Apply(new StageEvent(TurnStage.Searching, "Searching: current year", null));
        box.Apply(new StageEvent(
            TurnStage.Searching, null, [new RedStar.Base.WebSearchResult("Year Site", "https://year.example")]));
        box.Apply(new StageEvent(TurnStage.Searching, "Searching: weather forecast", null));
        box.Apply(new StageEvent(
            TurnStage.Searching, null, [new RedStar.Base.WebSearchResult("Weather Site", "https://weather.example")]));

        var text = RenderToPlainText(box);
        var yearLabelIndex = text.IndexOf("Searching: current year", StringComparison.Ordinal);
        var yearSiteIndex = text.IndexOf("Year Site", StringComparison.Ordinal);
        var weatherLabelIndex = text.IndexOf("Searching: weather forecast", StringComparison.Ordinal);
        var weatherSiteIndex = text.IndexOf("Weather Site", StringComparison.Ordinal);

        Assert.True(yearLabelIndex < yearSiteIndex);
        Assert.True(yearSiteIndex < weatherLabelIndex);
        Assert.True(weatherLabelIndex < weatherSiteIndex);
    }
}
