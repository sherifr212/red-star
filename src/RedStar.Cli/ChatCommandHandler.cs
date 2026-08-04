using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using BoxOfYellow.ConsoleMarkdownRenderer.Spectre;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RedStar.Base;
using RedStar.Base.Telemetry;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RedStar.Cli;

internal static class ChatCommandHandler
{
    /// <param name="agentFactory">
    /// Builds the <see cref="AIAgent"/> to chat with, given (options, modelId, instructions). Defaults to
    /// <see cref="RedStarChatClientFactory.Create"/>; tests can substitute a fake here without touching the
    /// network.
    /// </param>
    /// <param name="modelsClientFactory">
    /// Builds the <see cref="IModelsClient"/> used for auto-resolving a default model. Defaults to a real
    /// <see cref="ModelsClient"/>; tests can substitute a fake here without touching the network.
    /// </param>
    /// <param name="runId">
    /// Correlation ID tagged onto this run's root OTel span (<c>run.correlation.id</c>). Falls back to the
    /// <c>REDSTAR_RUN_ID</c> environment variable, then a generated GUID -- every child span created for the
    /// rest of this call (chat turns, outbound HTTP calls) shares this run's trace ID automatically since
    /// they're started while this method's <see cref="Activity"/> is <see cref="Activity.Current"/>.
    /// </param>
    public static async Task<int> RunAsync(
        RedStarOptions options,
        string? oneShotPrompt,
        string? systemPrompt,
        CancellationToken cancellationToken,
        Func<RedStarOptions, string, string?, AIAgent>? agentFactory = null,
        Func<RedStarOptions, IModelsClient>? modelsClientFactory = null,
        string? runId = null)
    {
        using var activity = RedStarTelemetry.ActivitySource.StartActivity("redstar.chat");
        runId ??= Environment.GetEnvironmentVariable("REDSTAR_RUN_ID") ?? Guid.NewGuid().ToString("N");
        activity?.SetTag("run.correlation.id", runId);

        var logger = RedStarTelemetry.CreateLogger("RedStar.Cli.ChatCommandHandler");
        logger.LogInformation("Starting redstar chat run {RunId}", runId);

        agentFactory ??= static (opts, modelId, instructions) => RedStarChatClientFactory.Create(opts, modelId, instructions);

        if (string.IsNullOrEmpty(options.ApiKey))
        {
            ConsoleOutput.Error.MarkupLine(
                "[yellow]Warning: no API key configured.[/] Unsloth Studio requires a bearer token for /v1 calls.\n" +
                "Generate one from the Unsloth Studio UI (Settings -> API Keys), then set it via\n" +
                "--api-key, the RedStar__ApiKey environment variable, or appsettings.local.json.\n");
        }

        var modelId = await ResolveModelAsync(options, cancellationToken, modelsClientFactory);
        if (modelId is null)
        {
            logger.LogWarning("Run {RunId} aborted: model resolution failed", runId);
            return 1;
        }

        logger.LogInformation("Run {RunId} resolved model {ModelId}", runId, modelId);

        AIAgent agent = agentFactory(options, modelId, systemPrompt);
        var session = new ChatSession(agent);

        if (!string.IsNullOrWhiteSpace(oneShotPrompt))
        {
            PrintUserMessageBox(oneShotPrompt);
            return await SendAndPrintAsync(session, oneShotPrompt, cancellationToken);
        }

        AnsiConsole.MarkupLine(
            $"[bold]RedStar chat[/] - model '[green]{Markup.Escape(modelId)}[/]'. Type 'exit' or press Ctrl+C to quit.");
        while (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.WriteLine();
            var line = ReadUserMessageBoxed();
            if (line is null)
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var exitCode = await SendAndPrintAsync(session, line, cancellationToken);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        return 0;
    }

    /// <summary>
    /// Draws an open "You" box (top border, then a "&gt; " prompt on the unclosed line) and reads one line
    /// with the console's normal line editor, then closes the box with a bottom border -- so the same box
    /// frames both the typing area and, once Enter is pressed, the submitted message.
    /// </summary>
    private static string? ReadUserMessageBoxed()
    {
        var width = GetBoxWidth();
        PrintBoxTopBorder(width, "You", Color.Cyan1);
        AnsiConsole.Markup($"[{Color.Cyan1.ToMarkup()}]│[/] > ");
        var line = Console.ReadLine();
        PrintBoxBottomBorder(width, Color.Cyan1);
        return line;
    }

    /// <summary>Prints a closed "You" box around a message that wasn't typed interactively (the one-shot prompt).</summary>
    private static void PrintUserMessageBox(string text)
    {
        var panel = new Panel(Markup.Escape(text))
            .Header("[cyan]You[/]")
            .RoundedBorder()
            .BorderColor(Color.Cyan1)
            .Expand();
        AnsiConsole.Write(panel);
    }

    /// <summary>
    /// One leg of a turn's lifecycle. Each is its own sealed-in-place box once the turn moves past it --
    /// see the "Multi-box rendering" remarks on <see cref="RenderStageBoxesAsync"/>. <see cref="Other"/> is
    /// the initial "nothing has happened yet" box every turn opens with, and doubles as the fallback for any
    /// future update shape this switch doesn't recognize; it never appears for a reason a user would
    /// otherwise see labeled, which is why it gets the deliberately-odd grey rather than one of the other
    /// three's saturated colors.
    /// </summary>
    private enum TurnStage
    {
        Other,
        Reasoning,
        Searching,
        Generating,
    }

    /// <summary>One piece of one stage's content: either a text delta to append, or a completed site list.</summary>
    private readonly record struct StageEvent(TurnStage Stage, string? TextDelta, IReadOnlyList<WebSearchResult>? Sites);

    /// <summary>Result of draining one stage's events until either a differently-staged event arrives
    /// (<see cref="NextEvent"/> set, <see cref="SplitForHeight"/> false) or the current box's estimated
    /// height crossed the safe-to-redraw threshold before the stage itself changed (<see cref="NextEvent"/>
    /// null, <see cref="SplitForHeight"/> true) -- the latter tells the caller to seal the current box and
    /// open a same-stage continuation instead of waiting for a real stage transition. See the remarks on
    /// <see cref="GetSafeBoxHeight"/> for why that matters.</summary>
    private readonly record struct DrainResult(StageEvent? NextEvent, bool SplitForHeight);

    private static async Task<int> SendAndPrintAsync(
        ChatSession session, string userText, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<StageEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var producer = ProduceStageEventsAsync(session, userText, channel.Writer, cancellationToken);
        var turnStopwatch = Stopwatch.StartNew();

        try
        {
            var hasResponseText = await RenderStageBoxesAsync(channel.Reader, turnStopwatch, cancellationToken);
            await producer; // never faults -- ProduceStageEventsAsync catches everything -- just observed for hygiene.

            AnsiConsole.WriteLine();

            if (!hasResponseText)
            {
                ConsoleOutput.Error.MarkupLine(
                    "[yellow]The model returned no response.[/] This can happen when a server-side tool call " +
                    "(e.g. web search) fails and the server drops the connection instead of finishing the reply. " +
                    "Try again or rephrase the prompt.\n");
            }

            return 0;
        }
        catch (Exception ex)
        {
            RedStarTelemetry.CreateLogger("RedStar.Cli.ChatCommandHandler").LogError(ex, "Error calling the model");
            ConsoleOutput.Error.MarkupLine($"\n[red]Error calling the model:[/]\n{Markup.Escape(ex.ToString())}\n");
            return 1;
        }
    }

    /// <summary>
    /// Drives the streamed turn and translates it into <see cref="StageEvent"/>s on <paramref name="writer"/>:
    /// reasoning text from <see cref="TextReasoningContent"/>, Unsloth tool-status labels and completed
    /// web-search hit lists via <see cref="RedStarChatClientFactory"/>'s raw-JSON extractors, and the final
    /// answer's text chunks. Runs concurrently with <see cref="RenderStageBoxesAsync"/> so the UI can start
    /// drawing a stage's box as soon as that stage's first event lands, rather than after the whole turn
    /// completes. Completes the channel (successfully or with the caught exception) when
    /// <see cref="ChatSession.SendAsync"/> returns or throws, which is how the reader side learns the turn is
    /// over.
    /// </summary>
    private static async Task ProduceStageEventsAsync(
        ChatSession session, string userText, ChannelWriter<StageEvent> writer, CancellationToken cancellationToken)
    {
        try
        {
            await session.SendAsync(
                userText,
                onTextChunk: chunk => writer.TryWrite(new StageEvent(TurnStage.Generating, chunk, null)),
                onUpdate: update =>
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is TextReasoningContent { Text.Length: > 0 } reasoning)
                        {
                            writer.TryWrite(new StageEvent(TurnStage.Reasoning, reasoning.Text, null));
                        }
                    }

                    var status = RedStarChatClientFactory.TryGetToolStatus(update);
                    if (status is not null)
                    {
                        writer.TryWrite(new StageEvent(TurnStage.Searching, status, null));
                    }

                    var sites = RedStarChatClientFactory.TryGetWebSearchResults(update);
                    if (sites is { Count: > 0 })
                    {
                        writer.TryWrite(new StageEvent(TurnStage.Searching, null, sites));
                    }
                },
                cancellationToken: cancellationToken);

            writer.Complete();
        }
        catch (Exception ex)
        {
            writer.Complete(ex);
        }
    }

    /// <summary>
    /// Multi-box rendering: rather than one panel that gets overwritten as the turn moves through phases,
    /// each run of same-stage events gets its own <see cref="AnsiConsole.Live"/> region, colored by
    /// <see cref="StageBox"/>'s stage-to-color mapping. Once a differently-staged event arrives, the current
    /// region's <c>StartAsync</c> delegate returns -- which leaves that box's last rendered frame on the
    /// terminal permanently (Spectre doesn't erase a completed Live region) -- and a new one opens for the
    /// new stage, seeded with the event that triggered the switch. The very first box is always
    /// <see cref="TurnStage.Other"/> (a plain waiting spinner), which closes the instant the first real event
    /// arrives, same as any other stage transition. Returns whether any <see cref="TurnStage.Generating"/>
    /// text was ever seen, for the caller's empty-response check.
    ///
    /// <paramref name="turnStopwatch"/> is shared across every box rather than each starting its own: the
    /// footer is "total time this request has taken so far," not "time this particular box has been open,"
    /// so it keeps counting up across stage transitions and only stops once the last box closes -- a box
    /// that finishes quickly shows the running total at that moment, not a misleadingly small "0m 0s".
    ///
    /// When stdout is redirected (no real console attached -- piped, `&gt; file`, a non-interactive
    /// debugger/CI runner), <see cref="AnsiConsole.Live"/> itself is unusable: it unconditionally toggles
    /// `Console.CursorVisible`, which throws `IOException: The handle is invalid` with no console attached
    /// -- a known, still-open Spectre.Console bug (spectreconsole/spectre.console#1393; a maintainer there
    /// confirms `app.exe &gt; file.txt` reproduces it). In that case each box is written once, fully formed,
    /// when its stage ends, instead of animated in place -- no flicker/cursor tricks needed since nothing
    /// after it gets overwritten.
    /// </summary>
    private static async Task<bool> RenderStageBoxesAsync(
        ChannelReader<StageEvent> reader, Stopwatch turnStopwatch, CancellationToken cancellationToken)
    {
        var isLive = !Console.IsOutputRedirected;
        var hasResponseText = false;
        StageEvent? next = null;
        var isFirstBox = true;
        var isContinuation = false;
        var lastStage = TurnStage.Other;
        string? chainCopyFilePath = null;
        var chainText = string.Empty;

        while (isFirstBox || next is not null || isContinuation)
        {
            var stage = isFirstBox ? TurnStage.Other : isContinuation ? lastStage : next!.Value.Stage;
            var box = new StageBox(
                stage, turnStopwatch, isContinuation,
                isContinuation ? chainCopyFilePath : null, isContinuation ? chainText : "");

            if (!isFirstBox && !isContinuation)
            {
                box.Apply(next!.Value);
            }

            isFirstBox = false;
            var splitForHeight = false;

            if (isLive)
            {
                using var gate = new SemaphoreSlim(1, 1);
                await AnsiConsole.Live(box.Render(final: false))
                    .StartAsync(async ctx =>
                    {
                        using var tickerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        var ticker = TickFooterAsync(ctx, gate, box, tickerCts.Token);

                        var drainResult = await DrainStageAsync(reader, box, gate, GetSafeBoxHeight(), cancellationToken, async () =>
                        {
                            await gate.WaitAsync();
                            try
                            {
                                ctx.UpdateTarget(box.Render(final: false));
                                ctx.Refresh();
                            }
                            finally
                            {
                                gate.Release();
                            }
                        });

                        next = drainResult.NextEvent;
                        splitForHeight = drainResult.SplitForHeight;

                        tickerCts.Cancel();
                        await ticker;

                        await gate.WaitAsync();
                        try
                        {
                            ctx.UpdateTarget(box.Render(final: true));
                            ctx.Refresh();
                        }
                        finally
                        {
                            gate.Release();
                        }
                    });
            }
            else
            {
                using var gate = new SemaphoreSlim(1, 1);
                var drainResult = await DrainStageAsync(reader, box, gate, maxHeight: null, cancellationToken, onChanged: null);
                next = drainResult.NextEvent;
                AnsiConsole.Write(box.Render(final: true));
            }

            if (box.Stage == TurnStage.Generating && box.HasText)
            {
                hasResponseText = true;
            }

            if (splitForHeight)
            {
                chainCopyFilePath = box.CopyFilePath;
                chainText += box.Text;
            }
            else
            {
                chainCopyFilePath = null;
                chainText = string.Empty;
            }

            lastStage = box.Stage;
            isContinuation = splitForHeight;
        }

        return hasResponseText;
    }

    /// <summary>Applies events to <paramref name="box"/> for as long as they belong to its stage, invoking
    /// <paramref name="onChanged"/> after each (used to redraw a live region; null when not live). Returns
    /// once a differently-staged event arrives, once <paramref name="box"/>'s estimated height crosses
    /// <paramref name="maxHeight"/> (see <see cref="GetSafeBoxHeight"/> -- <c>null</c> disables this check,
    /// used for the non-live/redirected-output path where there's no console viewport to protect), or once
    /// the channel is drained with no error -- see <see cref="DrainResult"/> for how those three outcomes
    /// are distinguished. <paramref name="gate"/> must be the same <see cref="SemaphoreSlim"/>
    /// <see cref="TickFooterAsync"/> and the live-region redraw acquire: <see cref="StageBox.Apply"/> mutates
    /// the box's internal <see cref="StringBuilder"/>, and <see cref="StageBox.Render"/>/
    /// <see cref="StageBox.EstimatedBodyLines"/> read it via <c>ToString()</c> from the ticker task on a
    /// timer -- StringBuilder isn't thread-safe, so without holding the same gate around the mutation too
    /// (not just the render/estimate calls), a concurrent Append/ToString pair can corrupt its internal
    /// chunk list and throw <see cref="ArgumentOutOfRangeException"/> ("chunkLength") out of
    /// <c>StringBuilder.ToString()</c>. A <see cref="SemaphoreSlim"/> is used instead of <c>lock</c> because
    /// this method and its callers are async: <c>lock</c>'s <c>Monitor</c> is thread-affine and can't be held
    /// across an <c>await</c>, whereas <c>SemaphoreSlim.WaitAsync</c>/<c>Release</c> works correctly as an
    /// async-friendly mutex (count of 1) across the awaits in <see cref="RenderStageBoxesAsync"/>'s
    /// live-region callback.
    /// </summary>
    private static async Task<DrainResult> DrainStageAsync(
        ChannelReader<StageEvent> reader, StageBox box, SemaphoreSlim gate, int? maxHeight, CancellationToken cancellationToken, Func<Task>? onChanged)
    {
        while (true)
        {
            var evt = await ReadNextAsync(reader, cancellationToken);
            if (evt is null)
            {
                return new DrainResult(null, false);
            }

            if (evt.Value.Stage != box.Stage)
            {
                return new DrainResult(evt, false);
            }

            await gate.WaitAsync();
            try
            {
                box.Apply(evt.Value);

                if (maxHeight is int max && box.EstimatedBodyLines() + PanelChromeLines > max)
                {
                    return new DrainResult(null, true);
                }
            }
            finally
            {
                gate.Release();
            }

            if (onChanged is not null)
            {
                await onChanged();
            }
        }
    }

    /// <summary>Reads one event, or null once the channel is drained with no error. A producer exception
    /// propagates as-is (not wrapped) so the caller's try/catch reports the real failure.</summary>
    private static async ValueTask<StageEvent?> ReadNextAsync(ChannelReader<StageEvent> reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>Redraws a box's footer once a second for as long as its <see cref="AnsiConsole.Live"/> region
    /// stays open, so the elapsed-time counter keeps ticking between content updates, not just because of
    /// them. Shares <paramref name="gate"/> with <see cref="DrainStageAsync"/> and the live-region redraw --
    /// see the remarks there for why a <see cref="SemaphoreSlim"/> guards this instead of <c>lock</c>.</summary>
    private static async Task TickFooterAsync(LiveDisplayContext ctx, SemaphoreSlim gate, StageBox box, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await gate.WaitAsync();
            try
            {
                box.Tick();
                ctx.UpdateTarget(box.Render(final: false));
                ctx.Refresh();
            }
            finally
            {
                gate.Release();
            }
        }
    }

    /// <summary>
    /// One sealed-in-place box: its own accumulated text and a stage-specific header/border color, but the
    /// elapsed-time footer reads a <see cref="Stopwatch"/> shared across every box in the turn -- see the
    /// remarks on <see cref="RenderStageBoxesAsync"/> for why. <see cref="TurnStage.Searching"/> status
    /// labels (e.g. "Searching: current year", then later "Reading: some-site.com") are kept as separate
    /// lines rather than concatenated, since each is a standalone label, not a token-by-token delta like
    /// reasoning/answer text is.
    /// </summary>
    private sealed class StageBox
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
        private IReadOnlyList<WebSearchResult>? _sites;
        private int _frame;
        private string? _copyFilePath;

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
            TurnStage stage, Stopwatch turnStopwatch, bool isContinuation = false,
            string? sharedCopyFilePath = null, string priorChainText = "")
        {
            Stage = stage;
            _stopwatch = turnStopwatch;
            _isContinuation = isContinuation;
            _copyFilePath = sharedCopyFilePath;
            _priorChainText = priorChainText;
        }

        public TurnStage Stage { get; }

        public bool HasText => _text.Length > 0;

        /// <summary>This box's own accumulated text (not including any earlier continuation box's text in
        /// the same chain) -- read by the caller once this box seals, to build the next continuation box's
        /// <c>priorChainText</c>.</summary>
        public string Text => _text.ToString();

        /// <summary>Set once <see cref="EnsureCopyFileUri"/> has run (i.e. once this box has rendered a
        /// final frame with text); otherwise null. Read by the caller once this box seals, to hand to the
        /// next same-stage continuation box as <c>sharedCopyFilePath</c>.</summary>
        public string? CopyFilePath => _copyFilePath;

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
                _sites = evt.Sites;
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

            var footerMarkup = final && HasText
                ? $"[grey]{Markup.Escape(ElapsedLabel())}[/]  [link={EnsureCopyFileUri()}]Copy[/]"
                : $"[grey]{Markup.Escape(ElapsedLabel())}[/]";
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
        /// file. A box only ever reaches <c>final: true</c> once its content has stopped changing (see the
        /// remarks on <see cref="RenderStageBoxesAsync"/>), so writing once per box here is correct, not just
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
        /// Rough estimate of how many rows this box's body will occupy once rendered, used by
        /// <see cref="ChatCommandHandler.DrainStageAsync"/> to decide whether the box needs to seal early
        /// -- see the remarks on <see cref="ChatCommandHandler.GetSafeBoxHeight"/> for why. There's no
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
            var innerWidth = Math.Max(1, GetBoxWidth() - 4);

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

        private static (string Header, Color Color) StageStyle(TurnStage stage, bool isContinuation)
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

    private static int GetBoxWidth() => Math.Clamp(Console.WindowWidth - 2, 20, 100);

    /// <summary>Rows a <see cref="StageBox"/>'s <see cref="Panel"/> chrome (top border, bottom border,
    /// footer row) always costs beyond its body -- used together with <see cref="GetSafeBoxHeight"/> to
    /// decide when a still-growing box needs to seal early.</summary>
    private const int PanelChromeLines = 3;

    /// <summary>How tall a live-redrawn box is allowed to get before it's sealed and a same-stage
    /// continuation box opens in its place, instead of letting <see cref="AnsiConsole.Live"/>'s default
    /// overflow handling crop it. Spectre's <c>Live</c> region defaults to
    /// <c>VerticalOverflow.Ellipsis</c>/<c>VerticalOverflowCropping.Top</c>: once a live-redrawn panel's
    /// height exceeds the console's row count, it silently drops the top lines and shows only the tail --
    /// those dropped lines are never written to the terminal at all while the box is still live (Spectre
    /// only force-writes the full content once, when the <c>Live</c> region closes). That means scrolling
    /// up past a still-streaming box's on-screen footprint shows whatever was already there before it (the
    /// previous box), not the current box's earlier content, since the current box never actually grew
    /// into that screen space yet. Staying comfortably under the console's height (a few rows of margin,
    /// floor of 6 for very short windows) means every box lands fully in real, permanent scrollback as
    /// it's sealed, and Spectre's crop path never needs to trigger.</summary>
    private static int GetSafeBoxHeight() => Math.Max(6, Console.WindowHeight - 3);

    private static void PrintBoxTopBorder(int width, string label, Color color)
    {
        var title = $" {label} ";
        var dashes = Math.Max(2, width - 2 - title.Length);
        var left = dashes / 2;
        var right = dashes - left;
        AnsiConsole.MarkupLine(
            $"[{color.ToMarkup()}]╭{new string('─', left)}[/]{Markup.Escape(title)}[{color.ToMarkup()}]{new string('─', right)}╮[/]");
    }

    private static void PrintBoxBottomBorder(int width, Color color) =>
        AnsiConsole.MarkupLine($"[{color.ToMarkup()}]╰{new string('─', width - 2)}╯[/]");

    /// <summary>
    /// Resolves and validates the model to chat with by checking it against the server's
    /// <c>/v1/models</c> list before any chat request is made -- this always makes the call
    /// (whether or not <see cref="RedStarOptions.DefaultModel"/> is set) so an unloaded or
    /// nonexistent model is caught here, with a clear message, instead of surfacing later as a
    /// misleading "the model returned no response" once the chat stream unexpectedly ends empty.
    /// See <see cref="ModelSelector.SelectDefault"/> for the resolution/trust rules.
    /// </summary>
    private static async Task<string?> ResolveModelAsync(
        RedStarOptions options, CancellationToken cancellationToken, Func<RedStarOptions, IModelsClient>? modelsClientFactory)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Checking available models...", async _ =>
            {
                var modelsClient = modelsClientFactory is null ? new ModelsClient(options) : modelsClientFactory(options);
                try
                {
                    var models = await modelsClient.ListAsync(cancellationToken);
                    var selected = ModelSelector.SelectDefault(models, options.DefaultModel);
                    if (selected is null)
                    {
                        if (string.IsNullOrWhiteSpace(options.DefaultModel))
                        {
                            ConsoleOutput.Error.MarkupLine(
                                "[red]No models are available on the server.[/] Load one in Unsloth Studio first.");
                        }
                        else
                        {
                            var available = models.Count > 0
                                ? string.Join(", ", models.Select(m => m.Id))
                                : "(none)";
                            ConsoleOutput.Error.MarkupLine(
                                $"[red]Model '{Markup.Escape(options.DefaultModel)}' was not found on the server.[/] " +
                                $"Available models: {Markup.Escape(available)}. Run 'redstar models' for details.");
                        }

                        return null;
                    }

                    return selected.Id;
                }
                catch (Exception ex)
                {
                    ConsoleOutput.Error.MarkupLine(
                        $"[red]Could not check available models ({Markup.Escape(ex.Message)}).[/] " +
                        "Check --endpoint/--api-key, or run 'redstar models'.");
                    return null;
                }
                finally
                {
                    (modelsClient as IDisposable)?.Dispose();
                }
            });
    }
}
