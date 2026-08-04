using Microsoft.Extensions.AI;
using RedStar.Base;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class ChatSessionTests
{
    [Fact]
    public void AddSystemPrompt_ThenAddUserMessage_PreservesOrder()
    {
        var session = new ChatSession();

        session.AddSystemPrompt("be terse");
        session.AddUserMessage("hi");

        Assert.Equal(2, session.Messages.Count);
        Assert.Equal(ChatRole.System, session.Messages[0].Role);
        Assert.Equal(ChatRole.User, session.Messages[1].Role);
    }

    [Fact]
    public async Task SendAsync_AccumulatesStreamedChunks_IntoFullResponseText()
    {
        var session = new ChatSession();
        session.AddUserMessage("hi");
        var client = new FakeChatClient("Hel", "lo, ", "world!");

        var result = await session.SendAsync(client);

        Assert.Equal("Hello, world!", result);
    }

    [Fact]
    public async Task SendAsync_InvokesOnTextChunk_ForEachStreamedPiece()
    {
        var session = new ChatSession();
        session.AddUserMessage("hi");
        var client = new FakeChatClient("a", "b", "c");
        var received = new List<string>();

        await session.SendAsync(client, onTextChunk: received.Add);

        Assert.Equal(["a", "b", "c"], received);
    }

    [Fact]
    public async Task SendAsync_AppendsAssistantMessage_ToHistory_OnSuccess()
    {
        var session = new ChatSession();
        session.AddUserMessage("hi");
        var client = new FakeChatClient("hello there");

        await session.SendAsync(client);

        Assert.Equal(2, session.Messages.Count);
        var assistantMessage = session.Messages[^1];
        Assert.Equal(ChatRole.Assistant, assistantMessage.Role);
        Assert.Equal("hello there", assistantMessage.Text);
    }

    [Fact]
    public async Task SendAsync_PassesFullHistory_ToTheChatClient()
    {
        var session = new ChatSession();
        session.AddSystemPrompt("be terse");
        session.AddUserMessage("hi");
        var client = new FakeChatClient("ok");

        await session.SendAsync(client);

        Assert.NotNull(client.LastMessages);
        Assert.Equal(2, client.LastMessages!.Count);
        Assert.Equal(ChatRole.System, client.LastMessages[0].Role);
        Assert.Equal(ChatRole.User, client.LastMessages[1].Role);
    }

    [Fact]
    public async Task SendAsync_DoesNotAppendAssistantMessage_WhenClientThrows()
    {
        var session = new ChatSession();
        session.AddUserMessage("hi");
        var client = FakeChatClient.Throwing(new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync(client));

        Assert.Single(session.Messages);
        Assert.Equal(ChatRole.User, session.Messages[0].Role);
    }

    [Fact]
    public async Task SendAsync_PropagatesTheClientsException()
    {
        var session = new ChatSession();
        session.AddUserMessage("hi");
        var client = FakeChatClient.Throwing(new InvalidOperationException("boom"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync(client));
        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task SendAsync_Throws_WhenChatClientIsNull()
    {
        var session = new ChatSession();

        await Assert.ThrowsAsync<ArgumentNullException>(() => session.SendAsync(null!));
    }
}
