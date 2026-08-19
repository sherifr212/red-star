using Microsoft.Extensions.AI;

namespace RedStar.UnitTest.Fakes;

internal sealed class FakeChatClient : IChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, IAsyncEnumerable<ChatResponseUpdate>> _respond;

    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public ChatOptions? LastOptions { get; private set; }

    public FakeChatClient(params string[] textChunks)
        : this(_ => StreamChunks(textChunks))
    {
    }

    public FakeChatClient(Func<IEnumerable<ChatMessage>, IAsyncEnumerable<ChatResponseUpdate>> respond)
    {
        _respond = respond;
    }

    public static FakeChatClient Throwing(Exception exception) => new(_ => ThrowingStream(exception));

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamChunks(IEnumerable<string> chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ThrowingStream(Exception exception)
    {
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162 // Unreachable code detected
        yield break;
#pragma warning restore CS0162
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        LastOptions = options;
        return _respond(messages);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("FakeChatClient only supports streaming for these tests.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}