using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using BoxOfYellow.ConsoleMarkdownRenderer.Spectre;

using RedStar.Base;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace RedStar.Cli.Rendering;

/// <summary>
/// One sealed-in-place box: its own accumulated text and a stage-specific header/border color, but the
/// elapsed-time footer reads a <see cref="Stopwatch"/> shared across every box in the turn -- see the
/// remarks on RenderStageBoxesAsync for why. <see cref="TurnStage.Searching"/> status
/// labels (e.g. "Searching: current year", then later "Reading: some-site.com") are kept as separate
/// lines rather than concatenated, since each is a standalone label, not a token-by-token delta like
/// reasoning/answer text is.
/// </summary>
internal sealed class StageBox
{
    private static readonly Spinner Spinner = Spinner.Known.Dots;

    /// <summary>
    /// Only the <see cref="TurnStage.Generating"/> (final-answer) box renders through this for now --
    /// see the remarks on <see cref="Render"/>. <c>Headers = []</c> turns off the library's default of
    /// rendering a level-1 heading (<c>#</c>) as large FIGlet ASCII art, which would blow out a chat
    /// panel's width; every heading level then falls back to the library's plain bold/underlined
    /// <c>Header</c> style instead. <c>WrapHeader = false</c> turns off re-wrapping rendered heading
    /// text in literal <c>#</c> characters (e.g. <c>## Title ##</c>) -- sensible for a document/pager
    /// view where that's the only visual cue for heading level, but redundant here since the bold +
    /// underline styling from <c>Header</c> already reads as a heading inside a chat box.
    /// </summary>
    private static readonly MarkdownRenderer MarkdownRenderer = new();
    private static readonly SpectreDisplayOptions MarkdownOptions = new() { Headers = [], WrapHeader = false };

    private readonly StringBuilder _text = new();
    private readonly Stopwatch _stopwatch;
    private readonly bool _isContinuation;
    private readonly string _priorChainText;
    /// <summary>
    /// Accumulates hits across every <see cref="StageEvent"/> with a non-empty <see cref="StageEvent.Sites"/>
    /// applied to this box, rather than being replaced by the latest one -- a single "Searching" box can
    /// span multiple <c>web_search</c> tool calls in one turn (e.g. a follow-up query), and each call's
    /// <c>tool_end</c> event only carries that call's own hits, so
    /// overwriting here would silently drop every earlier search's results from the box.
    /// </summary>
    private readonly List<WebSearchResult> _sites = [];
    private int _frame;
    private string? _copyFilePath;
    private int? _outputTokenCount;

    /// <param name="sharedCopyFilePath">
    /// The previous box's <see cref="CopyFilePath"/> when this box is a same-stage "(cont'd)"
    /// continuation, so this box's own <see cref="EnsureCopyFileUri"/> reuses that same temp file
    /// instead of minting a new one -- see the remarks there for why the whole chain needs to share
    /// one file/link.
    /// </param>
    /// <param name="priorChainText">Every earlier continuation box's full text, in order, for a
    /// continuation chain -- concatenated with this box's own text when (re)writing the shared copy
    /// file. Empty for a non-continuation (first-in-chain) box.</param>
    public StageBox(
        string stage, Stopwatch turnStopwatch, bool isContinuation = false,
        string? sharedCopyFilePath = null, string priorChainText = "")
    {
        Stage = stage;
        _stopwatch = turnStopwatch;
        _isContinuation = isContinuation;
        _copyFilePath = sharedCopyFilePath;
        _priorChainText = priorChainText;
    }

    public string Stage { get; }

    public bool HasText => _text.Length > 0;

    /// <summary>This box's own accumulated text (not including any earlier continuation box's text in
    /// the same chain) -- read by the caller once this box seals, to build the next continuation box's
    /// <c>priorChainText</c>.</summary>
    public string Text => _text.ToString();

    /// <summary>Set once <see cref="EnsureCopyFileUri"/> has run (i.e. once this box has rendered a
    /// final frame with text); otherwise null. Read by the caller once this box seals, to hand to the
    /// next same-stage continuation box as <c>sharedCopyFilePath</c>.</summary>
    public string? CopyFilePath => _copyFilePath;

    /// <summary>The whole turn's output-token count once a <c>UsageContent</c> update has landed on this
    /// box, else null. Read by the caller once this box seals, to log it via telemetry independently of
    /// whatever <see cref="TokensAndSpeedLabel"/> renders in the footer.</summary>
    public int? OutputTokenCount => _outputTokenCount;

    public void Tick() => _frame++;

    public void Apply(StageEvent evt)
    {
        if (!string.IsNullOrEmpty(evt.TextDelta))
        {
            if (Stage == TurnStage.Searching && _text.Length > 0)
            {
                _text.Append('\n');
            }

            _text.Append(evt.TextDelta);
        }

        if (evt.Sites is { Count: > 0 })
        {
            foreach (var site in evt.Sites)
            {
                if (!_sites.Contains(site))
                {
                    _sites.Add(site);
                }
            }
        }

        if (evt.OutputTokenCount is { } tokens)
        {
            _outputTokenCount = tokens;
        }
    }

    /// <summary>
    /// The answer is plain text as far as the model is concerned, but in practice models write it as
    /// markdown, so the <see cref="TurnStage.Generating"/> box renders it as such (headings, code
    /// fences, tables, etc.) instead of dumping it as escaped plain text. The other stages (Reasoning,
    /// Searching, the initial waiting box) are lower priority and still render as plain escaped text for
    /// now -- markdown there is mostly free-form model narration or our own status/site-list lines, not
    /// as valuable to format, and this keeps the change scoped to the one box that matters most while
    /// that approach gets proven out. Re-parses the whole accumulated answer on every redraw (there's no
    /// incremental markdown parser here), which means a still-open construct (an unclosed code fence, a
    /// dangling `**`) can render oddly until enough of it has streamed in to close -- an inherent
    /// tradeoff of rendering markdown live rather than only once the message is complete. A still-open
    /// fenced code block specifically can crash <c>BoxOfYellow.ConsoleMarkdownRenderer.Spectre</c> with a
    /// <see cref="NullReferenceException"/> (seen in practice, not just theorized) rather than just
    /// rendering oddly, so the render is wrapped below and falls back to plain escaped text for that one
    /// frame -- the next redraw, once more text has streamed in, normally parses fine.
    /// </summary>
    public Panel Render(bool final)
    {
        IRenderable body;
        if (Stage == TurnStage.Generating && _text.Length > 0)
        {
            body = TryRenderMarkdown(_text.ToString()) ?? new Markup(Markup.Escape(_text.ToString()));
        }
        else
        {
            body = new Markup(string.Join("\n\n", NonGeneratingBlocks()));
        }

        var footerMarkup = $"[grey]{Markup.Escape(ElapsedLabel())}[/]";
        if (final && HasText)
        {
            var tokensLabel = TokensAndSpeedLabel();
            if (tokensLabel is not null)
            {
                footerMarkup = $"[grey]{Markup.Escape(tokensLabel)}[/]  {footerMarkup}";
            }

            footerMarkup += $"  [link={EnsureCopyFileUri()}]Copy[/]";
        }

        var footer = Align.Right(new Markup(footerMarkup));
        var (header, color) = StageStyle(Stage, _isContinuation);
        return new Panel(new Rows(body, footer))
            .Header(header)
            .RoundedBorder()
            .BorderColor(color)
            .Expand();
    }

    /// <summary>
    /// Lazily writes this chain's full text (<see cref="_priorChainText"/> plus this box's own) to a
    /// temp <c>.txt</c> file the first time a final frame needs the "Copy" link. A same-stage "(cont'd)"
    /// continuation box is constructed with the previous box's <see cref="CopyFilePath"/> as
    /// <c>sharedCopyFilePath</c>, so it overwrites that same file with the combined text instead of
    /// minting a new one per box -- otherwise every continuation box's Copy link pointed at only its own
    /// fragment, not the whole logical message the height-based split cut into pieces. Because every box
    /// in the chain shares one path/URI, even a link an earlier, already-sealed box printed still
    /// resolves to the complete text once the chain finishes and this method's last call overwrites the
    /// file. A box only ever reaches <c>final: true</c> once its content has stopped changing, so writing
    /// once per box here is correct, not just
    /// an optimization. <c>.txt</c> rather than <c>.md</c> so the OS has a default handler for it on
    /// virtually any platform. No cleanup: relies on normal OS temp-directory housekeeping.
    /// </summary>
    private string EnsureCopyFileUri()
    {
        _copyFilePath ??= Path.Combine(Path.GetTempPath(), $"redstar-{Guid.NewGuid():N}.txt");
        File.WriteAllText(_copyFilePath, _priorChainText + _text);
        return new Uri(_copyFilePath).AbsoluteUri;
    }

    /// <summary>Null on any renderer failure -- see the remarks on <see cref="Render"/> for why a
    /// still-open construct can throw rather than just render oddly.</summary>
    private static IRenderable? TryRenderMarkdown(string text)
    {
        try
        {
            return MarkdownRenderer.Render(text, MarkdownOptions).Root;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private List<string> NonGeneratingBlocks()
    {
        var blocks = new List<string>();

        if (Stage == TurnStage.Other && _text.Length == 0)
        {
            blocks.Add($"{Spinner.Frames[_frame % Spinner.Frames.Count]} Waiting for the model...");
        }
        else
        {
            if (_text.Length > 0)
            {
                blocks.Add(Markup.Escape(_text.ToString()));
            }

            if (_sites is { Count: > 0 })
            {
                var lines = _sites.Select(
                    (site, index) => $"  [cyan]{index + 1}.[/] {Markup.Escape(site.Title)} [grey]-- {Markup.Escape(site.Url)}[/]");
                blocks.Add(string.Join("\n", lines));
            }
        }

        if (blocks.Count == 0)
        {
            blocks.Add(" ");
        }

        return blocks;
    }

    private string ElapsedLabel()
    {
        var elapsed = _stopwatch.Elapsed;
        return $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
    }

    /// <summary>
    /// The whole turn's output-token count and average tokens/second, or null when no <c>UsageContent</c>
    /// update ever arrived on this box, which happens when the server doesn't report usage. The trailing
    /// <c>UsageContent</c> update is the last event of the whole turn, so it lands on whichever box
    /// happens to still be open at that point -- for any response long enough to trip a height-based
    /// split (see <see cref="RenderStageBoxesAsync"/>'s <c>splitForHeight</c>), that's a same-stage
    /// "(cont'd)" continuation box, not the turn's first box for that stage. This intentionally does
    /// *not* exclude continuation boxes: since the whole turn only ever emits one <c>UsageContent</c>
    /// update, at most one box's <see cref="_outputTokenCount"/> is ever non-null, so showing it here
    /// whenever present can't duplicate the label across boxes -- excluding continuations would instead
    /// mean no box shows it at all for a turn that got split, which used to be the case here and left
    /// the footer silently missing for exactly the responses long enough to need it most. Speed divides
    /// by <see cref="_stopwatch"/>'s elapsed time (shared across the whole turn, not just this box) since
    /// the token count itself is a whole-turn total, not this box's own.
    /// </summary>
    private string? TokensAndSpeedLabel()
    {
        if (_outputTokenCount is not { } tokens)
        {
            return null;
        }

        var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        var speed = elapsedSeconds > 0 ? tokens / elapsedSeconds : 0;
        return $"{tokens} tok, {speed:0.0} tok/s";
    }

    /// <summary>
    /// Rough estimate of how many rows this box's body will occupy once rendered, used to decide whether the box needs to seal early.
    /// There's no
    /// cheap way to ask Spectre "how tall would this render" without its internal
    /// <c>RenderOptions</c>/measurement types (unreachable from outside <c>Spectre.Console.dll</c>),
    /// so this estimates from raw text instead: explicit newlines plus a width-based wrap estimate.
    /// For the markdown-rendered <see cref="TurnStage.Generating"/> box this is necessarily rough --
    /// headings, code fences, and tables can render taller than their raw text -- but markdown
    /// rendering only ever adds rows relative to raw text, never removes them, and estimating from
    /// escaped raw text (which is longer than the visible text once markup-escaped) only ever
    /// over-counts too. Both biases push the trigger earlier, never later, which is the safe
    /// direction: an occasional early seal is harmless, undercounting would reintroduce the bug this
    /// exists to fix.
    /// </summary>
    public int EstimatedBodyLines()
    {
        var innerWidth = Math.Max(1, ChatEngineConsoleHelper.GetBoxWidth() - 4);

        if (Stage == TurnStage.Generating && _text.Length > 0)
        {
            return Math.Max(1, EstimateWrappedLines(_text.ToString(), innerWidth));
        }

        var blocks = NonGeneratingBlocks();
        var lines = blocks.Count > 1 ? blocks.Count - 1 : 0; // blank-line separators from the "\n\n" join
        foreach (var block in blocks)
        {
            lines += EstimateWrappedLines(block, innerWidth);
        }

        return Math.Max(1, lines);
    }

    private static int EstimateWrappedLines(string text, int innerWidth)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var lines = 0;
        foreach (var line in text.Split('\n'))
        {
            lines += Math.Max(1, (int)Math.Ceiling(line.Length / (double)innerWidth));
        }

        return lines;
    }

    private static (string Header, Color Color) StageStyle(string stage, bool isContinuation)
    {
        var (label, markupColor, color) = stage switch
        {
            TurnStage.Reasoning => ("Reasoning", "skyblue1", Color.SkyBlue1),
            TurnStage.Searching => ("Searching", "gold1", Color.Gold1),
            TurnStage.Generating => ("Assistant", "magenta", Color.Magenta1),
            _ => ("Working", "grey", Color.Grey),
        };

        if (isContinuation)
        {
            label += " (cont'd)";
        }

        return ($"[{markupColor}]{label}[/]", color);
    }
}