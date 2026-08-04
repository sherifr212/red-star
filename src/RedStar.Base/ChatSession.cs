using System.Text;
using Microsoft.Extensions.AI;

namespace RedStar.Base;

/// <summary>
/// A conversation's message history plus the logic to stream one more turn through an
/// <see cref="IChatClient"/> and fold the result back into that history.
/// </summary>
public sealed class ChatSession
{
    private readonly List<ChatMessage> _messages = [];

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public void AddSystemPrompt(string prompt) => _messages.Add(new ChatMessage(ChatRole.System, prompt));

    public void AddUserMessage(string content) => _messages.Add(new ChatMessage(ChatRole.User, content));

    /// <summary>
    /// Streams a response for the current history through <paramref name="chatClient"/>,
    /// invoking <paramref name="onTextChunk"/> for each piece of text as it arrives. On success
    /// the full response is appended to the history as an assistant message. If the call throws,
    /// nothing is appended and the exception propagates to the caller.
    /// </summary>
    public async Task<string> SendAsync(
        IChatClient chatClient,
        ChatOptions? options = null,
        Action<string>? onTextChunk = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        var responseText = new StringBuilder();
        await foreach (var update in chatClient.GetStreamingResponseAsync(_messages, options, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                onTextChunk?.Invoke(update.Text);
                responseText.Append(update.Text);
            }
        }

        var fullText = responseText.ToString();
        _messages.Add(new ChatMessage(ChatRole.Assistant, fullText));
        return fullText;
    }
}
