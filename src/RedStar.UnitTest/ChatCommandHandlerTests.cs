using System.Diagnostics.Metrics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using RedStar.Base;
using RedStar.Base.Agents.Unsloth;
using RedStar.Base.Telemetry;
using RedStar.Cli;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class ChatCommandHandlerTests
{
    private static readonly RedStarOptions Options =
        new()
        {
            Agents = new AgentsOptions
            {
                Unsloth = new UnslothAgentOptions { ApiKey = "test-key", DefaultModel = "test-model" },
            },
        };

    private static Func<RedStarOptions, IModelsClient> ModelsClientFactory(bool loaded = true) =>
        _ => new FakeModelsClient([new ModelInfo("test-model", loaded)]);

    /// <summary>
    /// An options instance with no <c>DefaultModel</c> configured (the thing these tests actually exercise),
    /// but with an <c>ApiKey</c> set so <see cref="ChatCommandHandler.RunAsync"/> doesn't print its "no API
    /// key configured" warning to the console as a side effect of an unrelated test.
    /// </summary>
    private static RedStarOptions NoDefaultModelOptions() =>
        new() { Agents = new AgentsOptions { Unsloth = new UnslothAgentOptions { ApiKey = "test-key" } } };

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

    /// <summary>
    /// <see cref="ChatCommandHandler.RunAsync"/>'s <c>responseExtractor</c> parameter is the seam a future
    /// second agent would plug its own tool-status/search-result extraction into instead of the CLI branching
    /// on agent type -- this proves the seam actually drives the "Searching" stage end to end, using a fake
    /// that has no dependency on real Unsloth SSE JSON shapes (unlike <see cref="UnslothAgentResponseExtractor"/>,
    /// which only recognizes those).
    /// </summary>
    [Fact]
    public async Task RunAsync_OneShot_UsesInjectedResponseExtractor_ForSearchingStage()
    {
        Func<RedStarOptions, string, string?, AIAgent> agentFactory =
            (_, _, instructions) => new ChatClientAgent(new FakeChatClient("answer"), instructions: instructions);

        var toolStatusCalls = 0;
        var responseExtractor = new FakeAgentResponseExtractor(
            toolStatus: _ => Interlocked.Increment(ref toolStatusCalls) == 1 ? "Searching: test query" : null);

        var recordedStages = new List<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == RedStarTelemetry.ServiceName && instrument.Name == "redstar.stage.duration")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
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
            modelsClientFactory: ModelsClientFactory(),
            responseExtractor: responseExtractor);

        listener.Dispose();

        Assert.Equal(0, exitCode);
        Assert.Contains("Searching", recordedStages);
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

    private static Func<RedStarOptions, string, string?, AIAgent> NeverCalledAgentFactory =>
        (_, _, _) => throw new InvalidOperationException("Model resolution should have failed before the agent was built.");

    [Fact]
    public async Task RunAsync_FailsGracefully_WhenNoModelsAreLoaded()
    {
        Func<RedStarOptions, IModelsClient> modelsClientFactory = _ => new FakeModelsClient([new ModelInfo("test-model", Loaded: false)]);

        var exitCode = await ChatCommandHandler.RunAsync(
            Options,
            oneShotPrompt: "hi",
            systemPrompt: null,
            CancellationToken.None,
            agentFactory: NeverCalledAgentFactory,
            modelsClientFactory: modelsClientFactory);

        Assert.Equal(1, exitCode);
    }

    /// <summary>
    /// The follow-up requirement from issue #7: a configured default model that isn't loaded must fail
    /// loudly, even when a *different* model happens to be loaded -- silently substituting that other
    /// model would be misleading.
    /// </summary>
    [Fact]
    public async Task RunAsync_FailsGracefully_WhenConfiguredModelIsNotLoaded_EvenThoughAnotherModelIsLoaded()
    {
        Func<RedStarOptions, IModelsClient> modelsClientFactory = _ => new FakeModelsClient(
            [new ModelInfo("test-model", Loaded: false), new ModelInfo("other-model", Loaded: true)]);

        var exitCode = await ChatCommandHandler.RunAsync(
            Options,
            oneShotPrompt: "hi",
            systemPrompt: null,
            CancellationToken.None,
            agentFactory: NeverCalledAgentFactory,
            modelsClientFactory: modelsClientFactory);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_FailsGracefully_WhenMultipleModelsAreLoadedAndNoneIsConfigured()
    {
        var noDefaultOptions = NoDefaultModelOptions();
        Func<RedStarOptions, IModelsClient> modelsClientFactory = _ => new FakeModelsClient(
            [new ModelInfo("model-a", Loaded: true), new ModelInfo("model-b", Loaded: true)]);

        var exitCode = await ChatCommandHandler.RunAsync(
            noDefaultOptions,
            oneShotPrompt: "hi",
            systemPrompt: null,
            CancellationToken.None,
            agentFactory: NeverCalledAgentFactory,
            modelsClientFactory: modelsClientFactory);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_Succeeds_WhenOnlyOneModelIsLoadedAndNoneIsConfigured()
    {
        var noDefaultOptions = NoDefaultModelOptions();
        Func<RedStarOptions, IModelsClient> modelsClientFactory = _ => new FakeModelsClient([new ModelInfo("solo-model", Loaded: true)]);
        Func<RedStarOptions, string, string?, AIAgent> agentFactory =
            (_, modelId, instructions) =>
            {
                Assert.Equal("solo-model", modelId);
                return new ChatClientAgent(new FakeChatClient("hi"), instructions: instructions);
            };

        var exitCode = await ChatCommandHandler.RunAsync(
            noDefaultOptions,
            oneShotPrompt: "hi",
            systemPrompt: null,
            CancellationToken.None,
            agentFactory: agentFactory,
            modelsClientFactory: modelsClientFactory);

        Assert.Equal(0, exitCode);
    }
}
