using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using RedStar.Base.Telemetry;

namespace RedStar.Base.Agents.ClaudeCode;

public static class ClaudeCodeAgentFactory
{
    /// <summary>
    /// Builds an <see cref="AIAgent"/> backed by a local <c>claude</c> CLI subprocess instead of an
    /// OpenAI-compatible HTTP server -- unlike <c>UnslothAgentFactory.Create</c>/<c>LMStudioAgentFactory.Create</c>,
    /// there is no <c>OpenAIClient</c> here at all; <see cref="ClaudeCodeChatClient"/> is a from-scratch
    /// <see cref="IChatClient"/> that speaks the CLI's stream-json protocol directly (see
    /// <see cref="ClaudeCodeStreamJsonParser"/>). <paramref name="instructions"/> becomes the agent's system
    /// prompt the same way as the other two agents -- merged into <see cref="ChatOptions.Instructions"/> on
    /// every run by <see cref="ChatClientAgent"/>, and forwarded by <see cref="ClaudeCodeChatClient"/> to the
    /// subprocess as <c>--append-system-prompt</c> on its first turn only.
    /// </summary>
    /// <param name="modelId">
    /// Passed as <c>--model</c> when non-empty. Unlike the other two agents, empty is a legitimate value here
    /// (not an error) -- ClaudeCode has no "currently loaded models" concept for <see cref="ModelSelector"/>
    /// to resolve against, so <c>ChatCommandHandler</c> passes through
    /// <see cref="ClaudeCodeAgentOptions.DefaultModel"/> verbatim, including empty (meaning "let the CLI use
    /// its own configured default").
    /// </param>
    public static AIAgent Create(RedStarOptions options, string modelId, string? instructions = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(modelId);

        var claudeCode = options.Agents.ClaudeCode;

        RedStarTelemetry.CreateLogger("RedStar.Base.Agents.ClaudeCode.ClaudeCodeAgentFactory")
            .LogBuildingClaudeCodeAgent(modelId.Length == 0 ? "(CLI default)" : modelId, claudeCode.ProcessMode);

        IClaudeCodeProcessRunner runner = string.Equals(claudeCode.ProcessMode, ClaudeCodeProcessModes.LongLived, StringComparison.OrdinalIgnoreCase)
            ? new LongLivedClaudeCodeProcessRunner(claudeCode, modelId)
            : new PerTurnClaudeCodeProcessRunner(claudeCode, modelId);

        var chatClient = new ClaudeCodeChatClient(runner);
        var chatOptions = new ChatOptions { Instructions = instructions };

        return chatClient.AsAIAgent(new ChatClientAgentOptions { ChatOptions = chatOptions });
    }
}
