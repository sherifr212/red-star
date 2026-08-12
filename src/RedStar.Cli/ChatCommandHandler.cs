using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using BoxOfYellow.ConsoleMarkdownRenderer.Spectre;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RedStar.Base;
using RedStar.Base.Agents.GoogleAI;
using RedStar.Base.Agents.LMStudio;
using RedStar.Base.Agents.Unsloth;
using RedStar.Base.Telemetry;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RedStar.Cli;

internal static class ChatCommandHandler
{
    /// <param name="agentFactory">
    /// Builds the <see cref="AIAgent"/> to chat with, given (options, modelId, instructions). Defaults to
    /// <see cref="UnslothAgentFactory.Create"/> or <c>LMStudioAgentFactory.Create</c> depending on
    /// <see cref="RedStarOptions.Agent"/> (an explicit two-way switch, not a registry -- see
    /// <see cref="AgentNames"/>); tests can substitute a fake here without touching the network.
    /// </param>
    /// <param name="modelsClientFactory">
    /// Builds the <see cref="IModelsClient"/> used for auto-resolving a default model. Defaults to a real
    /// <see cref="ModelsClient"/> or <c>LMStudioModelsClient</c>, same per-agent switch as
    /// <paramref name="agentFactory"/>; tests can substitute a fake here without touching the network.
    /// </param>
    /// <param name="responseExtractor">
    /// Extracts tool-status labels and web-search hit lists from streamed updates (see
    /// <see cref="ProduceStageEventsAsync"/>). Defaults to <see cref="UnslothAgentResponseExtractor"/> or
    /// <c>LMStudioAgentResponseExtractor</c>, same per-agent switch as <paramref name="agentFactory"/>;
    /// tests can substitute a fake here without depending on real Unsloth SSE JSON shapes.
    /// </param>
    /// <param name="httpClientFactory">
    /// Factory for creating pre-configured HttpClient instances per agent. Only null in tests, which always
    /// supply agentFactory/modelsClientFactory directly.
    /// </param>
    /// <param name="handlerFactory">
    /// Factory for creating HttpMessageHandler instances that apply auth logic. Only null in tests.
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
        IHttpClientFactory? httpClientFactory = null,
        IHttpMessageHandlerFactory? handlerFactory = null,
        Func<RedStarOptions, string, string?, AIAgent>? agentFactory = null,
        Func<RedStarOptions, IModelsClient>? modelsClientFactory = null,
        IAgentResponseExtractor? responseExtractor = null,
        string? runId = null)
    {
        using var activity = RedStarTelemetry.ActivitySource.StartActivity("redstar.chat");
        runId ??= Environment.GetEnvironmentVariable("REDSTAR_RUN_ID") ?? Guid.NewGuid().ToString("N");
        activity?.SetTag("run.correlation.id", runId);

        var logger = RedStarTelemetry.CreateLogger("RedStar.Cli.ChatCommandHandler");
        logger.LogInformation("Starting redstar chat run {RunId}", runId);

        var active = ResolveActiveAgentSettings(options);
        var isLMStudio = active.AgentName == AgentNames.LMStudio;
        var isGoogleAI = active.AgentName == AgentNames.GoogleAI;

        // httpClientFactory/handlerFactory are only null in tests, which always supply agentFactory/
        // modelsClientFactory directly and so never evaluate these lambdas; production always resolves
        // ChatCommand through DI (see Program.cs), so both are non-null whenever these bodies actually run.
        agentFactory ??= isGoogleAI
            ? (opts, modelId, instructions) => GoogleAIAgentFactory.Create(
                httpClientFactory!.CreateClient(AgentNames.GoogleAI), opts, modelId, instructions)
            : isLMStudio
            ? (opts, modelId, instructions) => LMStudioAgentFactory.Create(
                BuildAgentHttpClient(handlerFactory!, AgentNames.LMStudio, opts.Agents.LMStudio.ApiKey), opts, modelId, instructions)
            : (opts, modelId, instructions) => UnslothAgentFactory.Create(
                BuildAgentHttpClient(handlerFactory!, AgentNames.Unsloth, opts.Agents.Unsloth.ApiKey), opts, modelId, instructions);
        modelsClientFactory ??= isGoogleAI
            ? opts => new GoogleAIModelsClient(httpClientFactory!.CreateClient(AgentNames.GoogleAI), opts)
            : isLMStudio
            ? opts => new LMStudioModelsClient(httpClientFactory!.CreateClient(AgentNames.LMStudio), opts)
            : opts => new ModelsClient(httpClientFactory!.CreateClient(AgentNames.Unsloth), opts);
        responseExtractor ??= isGoogleAI
            ? new GoogleAIAgentResponseExtractor()
            : isLMStudio ? new LMStudioAgentResponseExtractor() : new UnslothAgentResponseExtractor();

        if (string.IsNullOrEmpty(active.ApiKey) && !isLMStudio && !isGoogleAI)
        {
            ConsoleOutput.Error.MarkupLine(
                "[yellow]Warning: no API key configured.[/] Unsloth Studio requires a bearer token for /v1 calls.\n" +
                "Generate one from the Unsloth Studio UI (Settings -> API Keys), then set it via\n" +
                "--api-key, the RedStar__Agents__Unsloth__ApiKey environment variable, or appsettings.local.json.\n");
        }

        string configuredDefault;
        if (isGoogleAI)
        {
            configuredDefault = options.Agents.GoogleAI.DefaultModel;
        }
        else if (isLMStudio)
        {
            configuredDefault = options.Agents.LMStudio.DefaultModel;
        }
        else
        {
            configuredDefault = options.Agents.Unsloth.DefaultModel;
        }
        var allowJitLoad = isLMStudio; // Only LM Studio supports just-in-time loading
        var selection = await ResolveModelAsync(configuredDefault, allowJitLoad, options, cancellationToken, modelsClientFactory);
        if (!selection.Succeeded)
        {
            logger.LogWarning("Run {RunId} aborted: model resolution failed ({Reason})", runId, selection.ErrorMessage);
            return 1;
        }

        var modelId = selection.Model!.Id;
        var modelSource = selection.Source!.Value;

        if (selection.InfoMessage is not null)
        {
            ConsoleOutput.Error.MarkupLine($"[yellow]{Markup.Escape(selection.InfoMessage)}[/]");
        }

        logger.LogInformation("Run {RunId} resolved model {ModelId} via {ModelSource}", runId, modelId, modelSource);

        PrintStartupInfoBox(active, options.Otel, runId, modelId, modelSource, activity, logger);

        AIAgent agent = agentFactory(options, modelId, systemPrompt);
        var session = new ChatSession(agent);

        if (!string.IsNullOrWhiteSpace(oneShotPrompt))
        {
            PrintUserMessageBox(oneShotPrompt);
            return await SendAndPrintAsync(session, oneShotPrompt, responseExtractor, logger, cancellationToken);
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

            var exitCode = await SendAndPrintAsync(session, line, responseExtractor, logger, cancellationToken);
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
    private static class TurnStage
    {
        public const string Other = "Other";
        public const string Reasoning = "Reasoning";
        public const string Searching = "Searching";
        public const string Generating = "Generating";
    }

    /// <summary>One piece of one stage's content: a text delta to append, a completed site list, or a
    /// final output-token count (see <see cref="ProduceStageEventsAsync"/>'s <c>UsageContent</c> handling).
    /// </summary>
    private readonly record struct StageEvent(
        string Stage, string? TextDelta, IReadOnlyList<WebSearchResult>? Sites, int? OutputTokenCount = null);

    /// <summary>Result of draining one stage's events until either a differently-staged event arrives
    /// (<see cref="NextEvent"/> set, <see cref="SplitForHeight"/> false) or the current box's estimated
    /// height crossed the safe-to-redraw threshold before the stage itself changed (<see cref="NextEvent"/>
    /// null, <see cref="SplitForHeight"/> true) -- the latter tells the caller to seal the current box and
    /// open a same-stage continuation instead of waiting for a real stage transition. See the remarks on
    /// <see cref="GetSafeBoxHeight"/> for why that matters.</summary>
    private readonly record struct DrainResult(StageEvent? NextEvent, bool SplitForHeight);

    private static async Task<int> SendAndPrintAsync(
        ChatSession session, string userText, IAgentResponseExtractor responseExtractor, ILogger logger,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<StageEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        var producer = ProduceStageEventsAsync(session, userText, responseExtractor, channel.Writer, cancellationToken);
        var turnStopwatch = Stopwatch.StartNew();

        try
        {
            var hasResponseText = await RenderStageBoxesAsync(channel.Reader, turnStopwatch, logger, cancellationToken);
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
            logger.LogError(ex, "Error calling the model");
            ConsoleOutput.Error.MarkupLine($"\n[red]Error calling the model:[/]\n{Markup.Escape(ex.ToString())}\n");
            return 1;
        }
    }

    /// <summary>
    /// Drives the streamed turn and translates it into <see cref="StageEvent"/>s on <paramref name="writer"/>:
    /// reasoning text from <see cref="TextReasoningContent"/>, tool-status labels and completed web-search
    /// hit lists via <paramref name="responseExtractor"/>, the final answer's text chunks, and a trailing
    /// <see cref="UsageContent"/> update (when the server reports one -- see <c>UnslothAgentFactory</c>/
    /// <c>LMStudioAgentFactory</c>'s <c>stream_options.include_usage</c> request field) carrying the whole
    /// turn's output token count. That usage update arrives once, after every other event, with no stage of
    /// its own -- <paramref name="writer"/> tags it with <paramref name="lastStage"/>'s current value (the
    /// stage of whatever box happens to still be open), which is how the final box's footer in
    /// <see cref="StageBox"/> ends up with the total tokens/speed and every earlier box in the turn does not.
    /// Runs concurrently with <see cref="RenderStageBoxesAsync"/> so the UI can start drawing a stage's box
    /// as soon as that stage's first event lands, rather than after the whole turn completes. Completes the
    /// channel (successfully or with the caught exception) when <see cref="ChatSession.SendAsync"/> returns
    /// or throws, which is how the reader side learns the turn is over.
    /// </summary>
    private static async Task ProduceStageEventsAsync(
        ChatSession session, string userText, IAgentResponseExtractor responseExtractor,
        ChannelWriter<StageEvent> writer, CancellationToken cancellationToken)
    {
        var lastStage = TurnStage.Other;

        try
        {
            await session.SendAsync(
                userText,
                onTextChunk: chunk =>
                {
                    lastStage = TurnStage.Generating;
                    writer.TryWrite(new StageEvent(TurnStage.Generating, chunk, null));
                },
                onUpdate: update =>
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is TextReasoningContent { Text.Length: > 0 } reasoning)
                        {
                            lastStage = TurnStage.Reasoning;
                            writer.TryWrite(new StageEvent(TurnStage.Reasoning, reasoning.Text, null));
                        }
                        else if (content is UsageContent { Details.OutputTokenCount: { } outputTokens })
                        {
                            writer.TryWrite(new StageEvent(lastStage, null, null, (int)outputTokens));
                        }
                    }

                    var status = responseExtractor.TryGetToolStatus(update);
                    if (status is not null)
                    {
                        lastStage = TurnStage.Searching;
                        writer.TryWrite(new StageEvent(TurnStage.Searching, status, null));
                    }

                    var sites = responseExtractor.TryGetWebSearchResults(update);
                    if (sites is { Count: > 0 })
                    {
                        lastStage = TurnStage.Searching;
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
    /// <paramref name="turnStopwatch"/> doubles as the clock <see cref="RedStarTelemetry.StageDuration"/>
    /// measurements are derived from: a stage occurrence's boundaries are `!isContinuation` transitions
    /// (see the loop below), which only fire on a real stage change, not on a same-stage "(cont'd)" box
    /// opened purely because the previous one hit <see cref="GetSafeBoxHeight"/> -- so a stage that gets
    /// split across several boxes for rendering still records as one occurrence, not several.
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
        ChannelReader<StageEvent> reader, Stopwatch turnStopwatch, ILogger logger, CancellationToken cancellationToken)
    {
        var isLive = !Console.IsOutputRedirected;
        var hasResponseText = false;
        StageEvent? next = null;
        var isFirstBox = true;
        var isContinuation = false;
        var lastStage = TurnStage.Other;
        string? chainCopyFilePath = null;
        var chainText = string.Empty;
        var stageStartMs = 0L;
        string? pendingStage = null;

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

            if (!isContinuation)
            {
                if (pendingStage is { } completedStage)
                {
                    RecordStageDuration(completedStage, turnStopwatch.ElapsedMilliseconds - stageStartMs, logger);
                }

                stageStartMs = turnStopwatch.ElapsedMilliseconds;
                pendingStage = stage;
            }

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

            if (box.OutputTokenCount is { } outputTokens)
            {
                LogTokenUsage(outputTokens, turnStopwatch.Elapsed, logger);
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

        if (pendingStage is { } finalStage)
        {
            RecordStageDuration(finalStage, turnStopwatch.ElapsedMilliseconds - stageStartMs, logger);
        }

        return hasResponseText;
    }

    /// <summary>
    /// Records one <see cref="RedStarTelemetry.StageDuration"/> measurement for a completed stage
    /// occurrence, and logs it as a structured line so it also shows up in the OTEL logs view (the metric
    /// alone only surfaces in a dashboard's metrics/histogram view, not its logs view). <see
    /// cref="TurnStage.Other"/> is the initial "waiting for the first event" box, not a generation stage the
    /// model itself performs, so it's excluded from both -- only <see cref="TurnStage.Reasoning"/>,
    /// <see cref="TurnStage.Searching"/> (tool calling), and <see cref="TurnStage.Generating"/> (the final
    /// answer) are recorded/logged.
    /// </summary>
    private static void RecordStageDuration(string stage, long durationMs, ILogger logger)
    {
        if (stage == TurnStage.Other)
        {
            return;
        }

        RedStarTelemetry.StageDuration.Record(durationMs, new KeyValuePair<string, object?>("stage", stage));
        logger.LogInformation("Stage {Stage} completed in {StageDurationMs}ms", stage, durationMs);
    }

    /// <summary>
    /// Logs the whole turn's output-token count and average tokens/second once a <c>UsageContent</c> update
    /// has arrived (see <see cref="ProduceStageEventsAsync"/> and <see cref="StageBox.OutputTokenCount"/>) --
    /// a structured log line rather than only the <see cref="StageBox"/> footer, so the number is also
    /// recoverable from the OTEL logs, not just the terminal. <paramref name="elapsed"/> is the shared
    /// turn-wide stopwatch, matching the same total used for the footer's speed figure -- the token count is
    /// a whole-turn total, not this one box's share of it, so pairing it with anything narrower would be
    /// meaningless.
    /// </summary>
    private static void LogTokenUsage(int outputTokenCount, TimeSpan elapsed, ILogger logger)
    {
        var tokensPerSecond = elapsed.TotalSeconds > 0 ? outputTokenCount / elapsed.TotalSeconds : 0;
        logger.LogInformation(
            "Turn produced {OutputTokenCount} output tokens in {ElapsedMs}ms ({TokensPerSecond:0.0} tok/s)",
            outputTokenCount, elapsed.TotalMilliseconds, tokensPerSecond);
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
        /// <summary>
        /// Accumulates hits across every <see cref="StageEvent"/> with a non-empty <see cref="StageEvent.Sites"/>
        /// applied to this box, rather than being replaced by the latest one -- a single "Searching" box can
        /// span multiple <c>web_search</c> tool calls in one turn (e.g. a follow-up query), and each call's
        /// <c>tool_end</c> event only carries that call's own hits (see
        /// <see cref="RedStar.Base.Agents.Unsloth.UnslothAgentResponseExtractor.TryGetWebSearchResults"/>), so
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
        /// box (see <see cref="ChatCommandHandler.ProduceStageEventsAsync"/>), else null. Read by the caller
        /// once this box seals, to log it via <see cref="ChatCommandHandler.LogTokenUsage"/> regardless of
        /// whether this box itself is the one that renders it in its footer -- see
        /// <see cref="TokensAndSpeedLabel"/>'s "(cont'd)" exclusion, which only governs the footer, not
        /// telemetry.</summary>
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
        /// The whole turn's output-token count and average tokens/second, or null when neither applies.
        /// Null for a same-stage "(cont'd)" continuation box (<see cref="_isContinuation"/>) even once the
        /// chain's later box carries the count -- the count/speed describes the entire turn, not this one
        /// fragment's share of it, so it's only ever shown on a box that was never split for height. Null
        /// also whenever no <c>UsageContent</c> update ever arrived (see <see cref="ProduceStageEventsAsync"/>),
        /// which happens when the server doesn't report usage. Speed divides by <see cref="_stopwatch"/>'s
        /// elapsed time (shared across the whole turn, not just this box) since the token count itself is a
        /// whole-turn total, not this box's own.
        /// </summary>
        private string? TokensAndSpeedLabel()
        {
            if (_isContinuation || _outputTokenCount is not { } tokens)
            {
                return null;
            }

            var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
            var speed = elapsedSeconds > 0 ? tokens / elapsedSeconds : 0;
            return $"{tokens} tok, {speed:0.0} tok/s";
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

    private static HttpClient BuildAgentHttpClient(IHttpMessageHandlerFactory handlerFactory, string clientName, string? apiKey) =>
        new(new ConditionalAuthHandler(stripAuthHeader: string.IsNullOrEmpty(apiKey), handlerFactory.CreateHandler(clientName)));

    /// <summary>One agent's resolved connection settings for this run, picked from <see cref="RedStarOptions.Agent"/>
    /// once at the top of <see cref="RunAsync"/> instead of every call site reaching into
    /// <c>options.Agents.Unsloth</c>/<c>options.Agents.LMStudio</c> directly. <see cref="EnabledTools"/> is
    /// null for an agent with no such concept (LM Studio, GoogleAI), rather than an empty list, so <see cref="PrintStartupInfoBox"/>
    /// can tell "no tools enabled" apart from "not applicable" and omit the row entirely for the latter.</summary>
    private readonly record struct ActiveAgentSettings(string AgentName, string BaseUrl, string ApiKey, IReadOnlyList<string>? EnabledTools);

    private static ActiveAgentSettings ResolveActiveAgentSettings(RedStarOptions options) =>
        string.Equals(options.Agent, AgentNames.GoogleAI, StringComparison.OrdinalIgnoreCase)
            ? new ActiveAgentSettings(AgentNames.GoogleAI, options.Agents.GoogleAI.BaseUrl, options.Agents.GoogleAI.ApiKey, null)
            : string.Equals(options.Agent, AgentNames.LMStudio, StringComparison.OrdinalIgnoreCase)
            ? new ActiveAgentSettings(AgentNames.LMStudio, options.Agents.LMStudio.BaseUrl, options.Agents.LMStudio.ApiKey, null)
            : new ActiveAgentSettings(
                AgentNames.Unsloth, options.Agents.Unsloth.BaseUrl, options.Agents.Unsloth.ApiKey, options.Agents.Unsloth.EnabledTools);

    /// <summary>
    /// Resolves and validates the model to chat with by checking it against the server's model list before
    /// any chat request is made -- this always makes the call (whether or not <paramref name="configuredDefault"/>
    /// is set) so an unloaded or nonexistent model is caught here, with a clear message, instead of surfacing
    /// later as a misleading "the model returned no response" once the chat stream unexpectedly ends empty.
    /// See <see cref="ModelSelector.SelectDefault"/> for the resolution/trust rules, including what
    /// <paramref name="allowJitLoad"/> changes.
    /// </summary>
    private static async Task<ModelSelectionResult> ResolveModelAsync(
        string? configuredDefault, bool allowJitLoad, RedStarOptions options, CancellationToken cancellationToken,
        Func<RedStarOptions, IModelsClient> modelsClientFactory)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Checking available models...", async _ =>
            {
                var modelsClient = modelsClientFactory(options);
                try
                {
                    var models = await modelsClient.ListAsync(cancellationToken);
                    var result = ModelSelector.SelectDefault(models, configuredDefault, allowJitLoad);
                    if (!result.Succeeded)
                    {
                        ConsoleOutput.Error.MarkupLine($"[red]{Markup.Escape(result.ErrorMessage!)}[/]");
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    ConsoleOutput.Error.MarkupLine(
                        $"[red]Could not check available models ({Markup.Escape(ex.Message)}).[/] " +
                        "Check --endpoint/--api-key, or run 'redstar models'.");
                    return ModelSelectionResult.Fail($"Could not check available models: {ex.Message}");
                }
            });
    }

    /// <summary>
    /// Prints a boxed summary of this run's effective configuration -- which agent, endpoint, whether an
    /// API key is configured, the resolved model plus how it was picked (see
    /// <see cref="ModelSelectionSource"/>), every known tool's on/off state (when the active agent has such
    /// a concept -- see <see cref="UnslothTools.Known"/>), and telemetry export -- once per run, before any
    /// chat request goes out. Mirrors the same fields onto <paramref name="activity"/>'s tags
    /// (<c>redstar.config.*</c>) and one structured log line so this is recoverable from telemetry too, not
    /// just from the terminal -- the box itself is stdout-only and gone once the terminal scrolls past it.
    /// </summary>
    private static void PrintStartupInfoBox(
        ActiveAgentSettings active, OtelOptions otel, string runId, string modelId, ModelSelectionSource modelSource,
        Activity? activity, ILogger logger)
    {
        var apiKeyConfigured = !string.IsNullOrEmpty(active.ApiKey);
        var modelSourceLabel = modelSource switch
        {
            ModelSelectionSource.Explicit => "explicit (configured)",
            ModelSelectionSource.PendingJitLoad => "explicit (configured, loading on first request)",
            _ => "implicit (auto-detected)",
        };

        var table = new Table().Border(TableBorder.None).HideHeaders();
        table.AddColumn(new TableColumn(string.Empty).NoWrap());
        table.AddColumn(string.Empty);
        table.AddRow("[grey]Agent[/]", Markup.Escape(active.AgentName));
        table.AddRow("[grey]Run ID[/]", Markup.Escape(runId));
        table.AddRow("[grey]Endpoint[/]", Markup.Escape(active.BaseUrl));
        table.AddRow("[grey]API key[/]", apiKeyConfigured ? "[green]configured[/]" : "[yellow]not configured[/]");
        table.AddRow("[grey]Model[/]", $"[green]{Markup.Escape(modelId)}[/] [grey]({modelSourceLabel})[/]");
        if (active.EnabledTools is { } enabledTools)
        {
            table.AddRow("[grey]Tools[/]", FormatToolsSummary(enabledTools));
        }

        table.AddRow(
            "[grey]Telemetry[/]",
            otel.Enabled ? $"[green]enabled[/] -> {Markup.Escape(otel.Endpoint)}" : "disabled");

        var panel = new Panel(table)
            .Header("[bold]Startup configuration[/]")
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .Expand();
        AnsiConsole.Write(panel);

        activity?.SetTag("redstar.config.agent", active.AgentName);
        activity?.SetTag("redstar.config.endpoint", active.BaseUrl);
        activity?.SetTag("redstar.config.api_key_configured", apiKeyConfigured);
        activity?.SetTag("redstar.config.model", modelId);
        activity?.SetTag("redstar.config.model_source", modelSource.ToString());
        if (active.EnabledTools is { } enabledToolsForTag)
        {
            activity?.SetTag("redstar.config.enabled_tools", string.Join(",", enabledToolsForTag));
        }

        activity?.SetTag("redstar.config.telemetry_enabled", otel.Enabled);
        activity?.SetTag("redstar.config.telemetry_endpoint", otel.Endpoint);

        logger.LogInformation(
            "Startup configuration for run {RunId}: agent={Agent} endpoint={Endpoint} apiKeyConfigured={ApiKeyConfigured} " +
            "model={ModelId} modelSource={ModelSource} enabledTools={EnabledTools} " +
            "telemetryEnabled={TelemetryEnabled} telemetryEndpoint={TelemetryEndpoint}",
            runId, active.AgentName, active.BaseUrl, apiKeyConfigured, modelId, modelSource,
            active.EnabledTools is null ? "n/a" : string.Join(",", active.EnabledTools), otel.Enabled, otel.Endpoint);
    }

    /// <summary>
    /// Renders every documented Unsloth tool (<see cref="UnslothTools.Known"/>) plus any extra names present
    /// in <paramref name="enabledTools"/> that aren't in that list (a custom/undocumented tool name, since
    /// <see cref="RedStarOptions.EnabledTools"/> on <c>UnslothAgentOptions</c> is free-form) -- one per line,
    /// each tagged with its current enabled/disabled state, so the startup box always shows the full
    /// picture rather than only what happens to be turned on.
    /// </summary>
    private static string FormatToolsSummary(IReadOnlyList<string> enabledTools)
    {
        var enabledSet = new HashSet<string>(enabledTools, StringComparer.OrdinalIgnoreCase);
        var names = UnslothTools.Known
            .Concat(enabledTools.Where(t => !UnslothTools.Known.Contains(t, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return string.Join(
            "\n",
            names.Select(name => enabledSet.Contains(name)
                ? $"[green]{Markup.Escape(name)}: enabled[/]"
                : $"[grey]{Markup.Escape(name)}: disabled[/]"));
    }
}
