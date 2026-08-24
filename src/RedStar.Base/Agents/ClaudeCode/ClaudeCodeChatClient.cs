using System.Runtime.CompilerServices;

using Microsoft.Extensions.AI;

namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// <see cref="IChatClient"/> backed by the <c>claude</c> CLI subprocess instead of an OpenAI-compatible HTTP
/// endpoint -- the one agent in this project that doesn't go through the OpenAI SDK at all. All process I/O
/// is delegated to an <see cref="IClaudeCodeProcessRunner"/>; this class only turns
/// <see cref="ChatMessage"/>s into one turn's prompt text and turns raw stream-json lines back into
/// <see cref="ChatResponseUpdate"/>s (see <see cref="ClaudeCodeStreamJsonParser"/>).
///
/// Only the newest user message is ever actually sent to the subprocess -- <see cref="IChatClient"/>'s
/// contract hands every call the full conversation history (RedStar's <see cref="RedStarChatSession"/>/
/// <c>InMemoryChatHistoryProvider</c> owns it, same as for Unsloth/LM Studio), but ClaudeCode's own
/// session (established via <c>--session-id</c>/<c>--resume</c>, or simply by staying the same live process
/// in <see cref="ClaudeCodeProcessModes.LongLived"/>) already carries prior turns -- re-sending the whole
/// transcript as one flattened prompt would fight the CLI's own session model rather than use it. RedStar's
/// own copy of history becomes a local shadow copy (used for rendering/other agents), not the only copy, for
/// this agent specifically.
/// </summary>
public sealed class ClaudeCodeChatClient(IClaudeCodeProcessRunner runner) : IChatClient
{
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var userText = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
        if (string.IsNullOrEmpty(userText))
        {
            throw new InvalidOperationException("ClaudeCodeChatClient requires at least one non-empty user message.");
        }

        await foreach (var line in runner.SendAsync(userText, options?.Instructions, cancellationToken).ConfigureAwait(false))
        {
            if (ClaudeCodeStreamJsonParser.TryParseLine(line) is not { } parsed)
            {
                continue;
            }

            if (parsed.Result is { } result)
            {
                if (result.IsError)
                {
                    throw new ClaudeCodeProcessException(
                        0, $"claude reported an error (subtype '{result.Subtype ?? "unknown"}'): {result.ResultText ?? "no details"}");
                }

                var resultUpdate = new ChatResponseUpdate(ChatRole.Assistant, (string?)null) { RawRepresentation = parsed.RawJson };
                if (result.OutputTokens is { } outputTokens)
                {
                    resultUpdate.Contents.Add(new UsageContent(new UsageDetails { OutputTokenCount = outputTokens }));
                }

                yield return resultUpdate;
                yield break;
            }

            if (parsed.TextDelta is { } text)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, text) { RawRepresentation = parsed.RawJson };
            }
            else if (parsed.ToolUseName is not null)
            {
                // No text content -- ClaudeCodeAgentResponseExtractor recovers the tool name from
                // RawRepresentation, same pattern as UnslothAgentResponseExtractor's tool-status events.
                yield return new ChatResponseUpdate(ChatRole.Assistant, (string?)null) { RawRepresentation = parsed.RawJson };
            }
        }
    }

    /// <summary>Aggregates <see cref="GetStreamingResponseAsync"/> into one response. RedStar always calls
    /// the streaming path (<see cref="RedStarChatSession.SendAsync"/> -&gt; <c>AIAgent.RunStreamingAsync</c>), so this
    /// exists only to fulfill <see cref="IChatClient"/>'s contract, not because anything here exercises it.</summary>
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            updates.Add(update);
        }

        return updates.ToChatResponse();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <summary>Synchronously waits on <see cref="IClaudeCodeProcessRunner.DisposeAsync"/> to satisfy
    /// <see cref="IChatClient"/>'s sync-only <see cref="IDisposable"/> contract. Nothing in RedStar today
    /// actually calls this (see the remarks on <see cref="ClaudeCodeProcessModes.LongLived"/> for why that's
    /// not the only cleanup path) -- callers that can await should prefer <see cref="DisposeAsync"/> directly.</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public ValueTask DisposeAsync() => runner.DisposeAsync();
}