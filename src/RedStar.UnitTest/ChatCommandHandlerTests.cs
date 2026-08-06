using System.Diagnostics.Metrics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RedStar.Base;
using RedStar.Base.Telemetry;
using RedStar.Cli;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class ChatCommandHandlerTests
{
    private static readonly RedStarOptions Options = new() { DefaultModel = "test-model" };

    private static Func<RedStarOptions, IModelsClient> ModelsClientFactory(bool loaded = true) =>
        _ => new FakeModelsClient([new ModelInfo("test-model", loaded)]);

    /// <summary>Per the "Startup changes" requirements, no loaded models at all is a hard failure --
    /// there's nothing on-demand-loadable to fall back to anymore, even though "test-model" is known to
    /// the server and matches the configured default.</summary>
    [Fact]
    public async Task RunAsync_Fails_WhenNoModelIsLoaded()
    {
        Func<RedStarOptions, string, string?, AIAgent> agentFactory =
            (_, _, instructions) => new ChatClientAgent(new FakeChatClient("unused"), instructions: instructions);

        var exitCode = await ChatCommandHandler.RunAsync(
            Options,
            oneShotPrompt: "hi",
            systemPrompt: null,
            CancellationToken.None,
            agentFactory: agentFactory,
            modelsClientFactory: ModelsClientFactory(loaded: false));

        Assert.Equal(1, exitCode);
    }

    /// <summary>With more than one model loaded, an unconfigured/mismatched default is ambiguous and must
    /// fail rather than silently picking one.</summary>
    [Fact]
    public async Task RunAsync_Fails_WhenMultipleModelsAreLoadedAndConfiguredDefaultIsNotAmongThem()
    {
        Func<RedStarOptions, string, string?, AIAgent> agentFactory =
            (_, _, instructions) => new ChatClientAgent(new FakeChatClient("unused"), instructions: instructions);
        Func<RedStarOptions, IModelsClient> modelsClientFactory =
            _ => new FakeModelsClient([new ModelInfo("other-model-1", true), new ModelInfo("other-model-2", true)]);

        var exitCode = await ChatCommandHandler.RunAsync(
            Options,
            oneShotPrompt: "hi",
            systemPrompt: null,
            CancellationToken.None,
            agentFactory: agentFactory,
            modelsClientFactory: modelsClientFactory);

        Assert.Equal(1, exitCode);
    }

    /// <summary>Exactly one loaded model wins even when it disagrees with the configured default -- the
    /// turn should still go through successfully against that one loaded model.</summary>
    [Fact]
    public async Task RunAsync_Succeeds_UsingTheOnlyLoadedModel_EvenWhenItDiffersFromTheConfiguredDefault()
    {
        Func<RedStarOptions, string, string?, AIAgent> agentFactory =
            (_, _, instructions) => new ChatClientAgent(new FakeChatClient("hi there"), instructions: instructions);
        Func<RedStarOptions, IModelsClient> modelsClientFactory =
            _ => new FakeModelsClient([new ModelInfo("a-different-model", true)]);

        var exitCode = await ChatCommandHandler.RunAsync(
            Options,
            oneShotPrompt: "hi",
            systemPrompt: null,
            CancellationToken.None,
            agentFactory: agentFactory,
            modelsClientFactory: modelsClientFactory);

        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// BoxOfYellow.ConsoleMarkdownRenderer.Spectre (0.12.3) throws a <see cref="NullReferenceException"/>
    /// when asked to render a fenced code block that never closes/never gets any content -- confirmed by
    /// reproducing it directly against the package, not just inferred from the stack trace. A streamed
    /// answer that is (or, mid-stream, currently amounts to) a bare "```" is exactly that shape, and it's
    /// the one-shot path (<see cref="ChatCommandHandler.RunAsync"/> with <c>oneShotPrompt</c> set) where
    /// this was originally observed crashing the whole turn instead of degrading gracefully. This proves
    /// the fallback in <c>StageBox.Render()</c> actually prevents that: the turn must still succeed
    /// (exit code 0) rather than being reported as "Error calling the model".
    /// </summary>
    [Fact]
    public async Task RunAsync_OneShot_SucceedsWhenTheAnswerIsAnUnterminatedCodeFence()
    {
        Func<RedStarOptions, string, string?, AIAgent> agentFactory =
            (_, _, instructions) => new ChatClientAgent(new FakeChatClient("```"), instructions: instructions);

        var exitCode = await ChatCommandHandler.RunAsync(
            Options,
            oneShotPrompt: "hi",
            systemPrompt: null,
            CancellationToken.None,
            agentFactory: agentFactory,
            modelsClientFactory: ModelsClientFactory());

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_OneShot_SucceedsWithAnOrdinaryAnswer()
    {
        Func<RedStarOptions, string, string?, AIAgent> agentFactory =
            (_, _, instructions) => new ChatClientAgent(new FakeChatClient("Hello, ", "world!"), instructions: instructions);

        var exitCode = await ChatCommandHandler.RunAsync(
            Options,
            oneShotPrompt: "hi",
            systemPrompt: null,
            CancellationToken.None,
            agentFactory: agentFactory,
            modelsClientFactory: ModelsClientFactory());

        Assert.Equal(0, exitCode);
    }

    /// <summary>
    /// The model can revisit the same stage more than once in a single turn (e.g. reason, then answer, then
    /// reason again before finishing the answer). Each occurrence must record its own
    /// <see cref="RedStarTelemetry.StageDuration"/> measurement -- tagged with the plain stage name every
    /// time, never a "(2)"-style suffix -- rather than being merged/summed into one "Reasoning" total, and
    /// the measurements must come out in the order the stages actually happened. The initial "waiting for
    /// the first event" box (<c>TurnStage.Other</c>) isn't a stage the model itself performs, so it must not
    /// be recorded at all.
    /// </summary>
    [Fact]
    public async Task RunAsync_OneShot_RecordsOneStageDurationPerOccurrence_NotCompounded()
    {
        Func<RedStarOptions, string, string?, AIAgent> agentFactory =
            (_, _, instructions) => new ChatClientAgent(new FakeChatClient(_ => MixedStageStream()), instructions: instructions);

        var recordedStages = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == RedStarTelemetry.ServiceName && instrument.Name == "redstar.stage.duration")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, measurement, tags, _) =>
        {
            Assert.True(measurement >= 0);
            var stage = tags.ToArray().First(t => t.Key == "stage").Value?.ToString();
            lock (recordedStages)
            {
                recordedStages.Add(stage!);
            }
        });
        listener.Start();

        var exitCode = await ChatCommandHandler.RunAsync(
            Options,
            oneShotPrompt: "hi",
            systemPrompt: null,
            CancellationToken.None,
            agentFactory: agentFactory,
            modelsClientFactory: ModelsClientFactory());

        listener.Dispose();

        Assert.Equal(0, exitCode);
        Assert.Equal(["Reasoning", "Generating", "Reasoning", "Generating"], recordedStages);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> MixedStageStream()
    {
        yield return new ChatResponseUpdate { Contents = [new TextReasoningContent("thinking one")] };
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, "answer one ");
        await Task.Yield();
        yield return new ChatResponseUpdate { Contents = [new TextReasoningContent("thinking two")] };
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, "answer two");
        await Task.Yield();
    }
}
