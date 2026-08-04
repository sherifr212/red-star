using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using BoxOfYellow.ConsoleMarkdownRenderer.Spectre;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RedStar.Base;
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
    public static async Task<int> RunAsync(
        RedStarOptions options,
        string? oneShotPrompt,
        string? systemPrompt,
        CancellationToken cancellationToken,
        Func<RedStarOptions, string, string?, AIAgent>? agentFactory = null,
        Func<RedStarOptions, IModelsClient>? modelsClientFactory = null)
    {
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
            return 1;
        }

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
            ConsoleOutput.Error.MarkupLine($"\n[red]Error calling the model: {Markup.Escape(ex.Message)}[/]");
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

        while (isFirstBox || next is not null)
        {
            var box = new StageBox(isFirstBox ? TurnStage.Other : next!.Value.Stage, turnStopwatch);

            if (!isFirstBox)
            {
                box.Apply(next!.Value);
            }

            isFirstBox = false;

            if (isLive)
            {
                var sync = new object();
                await AnsiConsole.Live(box.Render())
                    .StartAsync(async ctx =>
                    {
                        using var tickerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        var ticker = TickFooterAsync(ctx, sync, box, tickerCts.Token);

                        next = await DrainStageAsync(reader, box, cancellationToken, () =>
                        {
                            lock (sync)
                            {
                                ctx.UpdateTarget(box.Render());
                                ctx.Refresh();
                            }
                        });

                        tickerCts.Cancel();
                        await ticker;

                        lock (sync)
                        {
                            ctx.UpdateTarget(box.Render());
                            ctx.Refresh();
                        }
                    });
            }
            else
            {
                next = await DrainStageAsync(reader, box, cancellationToken, onChanged: null);
                AnsiConsole.Write(box.Render());
            }

            if (box.Stage == TurnStage.Generating && box.HasText)
            {
                hasResponseText = true;
            }
        }

        return hasResponseText;
    }

    /// <summary>Applies events to <paramref name="box"/> for as long as they belong to its stage, invoking
    /// <paramref name="onChanged"/> after each (used to redraw a live region; null when not live). Returns
    /// the first differently-staged event once the stage ends, or null once the channel is drained with no
    /// error.</summary>
    private static async Task<StageEvent?> DrainStageAsync(
        ChannelReader<StageEvent> reader, StageBox box, CancellationToken cancellationToken, Action? onChanged)
    {
        while (true)
        {
            var evt = await ReadNextAsync(reader, cancellationToken);
            if (evt is null)
            {
                return null;
            }

            if (evt.Value.Stage != box.Stage)
            {
                return evt;
            }

            box.Apply(evt.Value);
            onChanged?.Invoke();
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
    /// stays open, so the elapsed-time counter keeps ticking between content updates, not just because of them.</summary>
    private static async Task TickFooterAsync(LiveDisplayContext ctx, object sync, StageBox box, CancellationToken cancellationToken)
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

            lock (sync)
            {
                box.Tick();
                ctx.UpdateTarget(box.Render());
                ctx.Refresh();
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
        private IReadOnlyList<WebSearchResult>? _sites;
        private int _frame;

        public StageBox(TurnStage stage, Stopwatch turnStopwatch)
        {
            Stage = stage;
            _stopwatch = turnStopwatch;
        }

        public TurnStage Stage { get; }

        public bool HasText => _text.Length > 0;

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
        /// tradeoff of rendering markdown live rather than only once the message is complete.
        /// </summary>
        public Panel Render()
        {
            IRenderable body;
            if (Stage == TurnStage.Generating && _text.Length > 0)
            {
                var result = MarkdownRenderer.Render(_text.ToString(), MarkdownOptions);
                body = result.Root ?? new Markup(Markup.Escape(_text.ToString()));
            }
            else
            {
                body = new Markup(string.Join("\n\n", NonGeneratingBlocks()));
            }

            var footer = Align.Right(new Markup($"[grey]{Markup.Escape(ElapsedLabel())}[/]"));
            var (header, color) = StageStyle(Stage);
            return new Panel(new Rows(body, footer))
                .Header(header)
                .RoundedBorder()
                .BorderColor(color)
                .Expand();
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

        private static (string Header, Color Color) StageStyle(TurnStage stage) => stage switch
        {
            TurnStage.Reasoning => ("[skyblue1]Reasoning[/]", Color.SkyBlue1),
            TurnStage.Searching => ("[gold1]Searching[/]", Color.Gold1),
            TurnStage.Generating => ("[magenta]Assistant[/]", Color.Magenta1),
            _ => ("[grey]Working[/]", Color.Grey),
        };
    }

    private static int GetBoxWidth() => Math.Clamp(Console.WindowWidth - 2, 20, 100);

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
