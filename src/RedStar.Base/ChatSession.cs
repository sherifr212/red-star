using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace RedStar.Base;

/// <summary>
/// One conversation bound to a single <see cref="AIAgent"/>. Lazily creates the agent's
/// <see cref="AgentSession"/> on first send and streams each turn through it, letting the
/// agent's chat history provider own message history instead of tracking it locally.
/// </summary>
public sealed class ChatSession
{
    private readonly AIAgent _agent;
    private AgentSession? _session;

    public ChatSession(AIAgent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agent = agent;
    }

    /// <summary>
    /// The conversation history recorded so far, or empty before the first <see cref="SendAsync"/> call.
    /// </summary>
    public IReadOnlyList<ChatMessage> Messages =>
        _session is not null && AgentSessionExtensions.TryGetInMemoryChatHistory(_session, out var messages)
            ? messages
            : [];

    /// <summary>
    /// Streams a response for <paramref name="userText"/> through the agent, invoking
    /// <paramref name="onTextChunk"/> for each piece of text as it arrives and <paramref name="onUpdate"/>
    /// for every raw streamed update (including ones with no text, e.g. tool-call progress). On success
    /// the full turn is appended to history by the agent's chat history provider. If the call throws,
    /// nothing is appended and the exception propagates to the caller.
    /// </summary>
    public async Task<string> SendAsync(
        string userText,
        Action<string>? onTextChunk = null,
        Action<AgentResponseUpdate>? onUpdate = null,
        CancellationToken cancellationToken = default)
    {
        _session ??= await _agent.CreateSessionAsync(cancellationToken);

        var responseText = new StringBuilder();
        await foreach (var update in _agent.RunStreamingAsync(userText, _session, options: null, cancellationToken))
        {
            onUpdate?.Invoke(update);

            if (!string.IsNullOrEmpty(update.Text))
            {
                onTextChunk?.Invoke(update.Text);
                responseText.Append(update.Text);
            }
        }

        return responseText.ToString();
    }
}
