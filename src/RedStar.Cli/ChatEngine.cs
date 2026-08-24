using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using RedStar.Base;
using RedStar.Base.Telemetry;
using RedStar.Cli.Rendering;

using Spectre.Console;

namespace RedStar.Cli;

internal static class ChatEngine
{
    public static async Task<int> SendAndPrintAsync(
        RedStarChatSession session, string userText, IAgentResponseExtractor responseExtractor, ILogger logger,
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
    /// channel (successfully or with the caught exception) when <see cref="RedStarChatSession.SendAsync"/> returns
    /// or throws, which is how the reader side learns the turn is over.
    /// </summary>
    private static async Task ProduceStageEventsAsync(
        RedStarChatSession session, string userText, IAgentResponseExtractor responseExtractor,
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

                        var drainResult = await DrainStageAsync(reader, box, gate, ChatEngineConsoleHelper.GetSafeBoxHeight(), cancellationToken, async () =>
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
    /// <paramref name="maxHeight"/> (see <see cref="ChatEngineConsoleHelper.GetSafeBoxHeight"/> -- <c>null</c> disables this check,
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

                if (maxHeight is int max && box.EstimatedBodyLines() + ChatEngineConsoleHelper.PanelChromeLines > max)
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

    /// <summary>Reads one event, or null once the channel is drained with no error. <see cref="Channel{T}"/>
    /// surfaces a faulted <see cref="ChannelWriter{T}.Complete(Exception)"/> the same way it surfaces a
    /// normal, error-free completion once nothing is left to read: both throw <see cref="ChannelClosedException"/>
    /// out of <see cref="ChannelReader{T}.ReadAsync"/>, with the producer's real exception only reachable via
    /// <see cref="Exception.InnerException"/> in the faulted case (<c>null</c> in the normal case). Swallowing
    /// every <see cref="ChannelClosedException"/> unconditionally -- as this used to do -- silently discarded
    /// <see cref="ProduceStageEventsAsync"/>'s real exception (e.g. an HTTP failure from the underlying
    /// <c>IChatClient</c>) and made every producer failure look identical to an ordinary empty response, so
    /// the caller reported "the model returned no response" instead of the actual error. Only the true
    /// no-error case (<see cref="ChannelClosedException.InnerException"/> is <c>null</c>) is swallowed here;
    /// a faulted completion rethrows its inner exception, preserving its original type/stack via
    /// <see cref="ExceptionDispatchInfo"/>, so <see cref="SendAndPrintAsync"/>'s try/catch reports it.</summary>
    private static async ValueTask<StageEvent?> ReadNextAsync(ChannelReader<StageEvent> reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException ex)
        {
            if (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }

            return null;
        }
    }

    /// <summary>Redraws a box's footer once a second for as long as its <see cref="AnsiConsole.Live"/> region
    /// stays open, so the elapsed-time counter keeps ticking between content updates, not just because of
    /// them. Shares <paramref name="gate"/> with <see cref="DrainStageAsync"/> and the live-region redraw --
    /// see the remarks there for why a <see cref="SemaphoreSlim"/> guards this instead of <c>lock</c>.</summary>
    private static async Task TickFooterAsync(Spectre.Console.LiveDisplayContext ctx, SemaphoreSlim gate, StageBox box, CancellationToken cancellationToken)
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
}