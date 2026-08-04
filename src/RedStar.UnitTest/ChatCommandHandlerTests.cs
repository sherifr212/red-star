using Microsoft.Agents.AI;
using RedStar.Base;
using RedStar.Cli;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class ChatCommandHandlerTests
{
    private static readonly RedStarOptions Options = new() { DefaultModel = "test-model" };

    private static Func<RedStarOptions, IModelsClient> ModelsClientFactory(bool loaded = true) =>
        _ => new FakeModelsClient([new ModelInfo("test-model", loaded)]);

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
}
