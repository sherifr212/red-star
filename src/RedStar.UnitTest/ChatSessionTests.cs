using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

using RedStar.Base;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class ChatSessionTests
{
    [Fact]
    public async Task SendAsync_AccumulatesStreamedChunks_IntoFullResponseText()
    {
        var agent = new ChatClientAgent(new FakeChatClient("Hel", "lo, ", "world!"));
        var session = new ChatSession(agent);

        var result = await session.SendAsync("hi");

        Assert.Equal("Hello, world!", result);
    }

    [Fact]
    public async Task SendAsync_InvokesOnTextChunk_ForEachStreamedPiece()
    {
        var agent = new ChatClientAgent(new FakeChatClient("a", "b", "c"));
        var session = new ChatSession(agent);
        var received = new List<string>();

        await session.SendAsync("hi", onTextChunk: received.Add);

        Assert.Equal(["a", "b", "c"], received);
    }

    [Fact]
    public async Task SendAsync_AppendsBothTurns_ToHistory_OnSuccess()
    {
        var agent = new ChatClientAgent(new FakeChatClient("hello there"));
        var session = new ChatSession(agent);

        await session.SendAsync("hi");

        Assert.Equal(2, session.Messages.Count);
        Assert.Equal(ChatRole.User, session.Messages[0].Role);
        Assert.Equal("hi", session.Messages[0].Text);
        Assert.Equal(ChatRole.Assistant, session.Messages[^1].Role);
        Assert.Equal("hello there", session.Messages[^1].Text);
    }

    [Fact]
    public async Task SendAsync_MergesInstructions_IntoChatOptions()
    {
        var client = new FakeChatClient("ok");
        var agent = new ChatClientAgent(client, instructions: "be terse");
        var session = new ChatSession(agent);

        await session.SendAsync("hi");

        Assert.Equal("be terse", client.LastOptions?.Instructions);
    }

    [Fact]
    public async Task SendAsync_PassesTheUserMessage_ToTheChatClient()
    {
        var client = new FakeChatClient("ok");
        var agent = new ChatClientAgent(client);
        var session = new ChatSession(agent);

        await session.SendAsync("hi");

        Assert.NotNull(client.LastMessages);
        Assert.Contains(client.LastMessages!, m => m.Role == ChatRole.User && m.Text == "hi");
    }

    [Fact]
    public async Task SendAsync_PropagatesTheClientsException_AndDoesNotGrowHistory()
    {
        var agent = new ChatClientAgent(FakeChatClient.Throwing(new InvalidOperationException("boom")));
        var session = new ChatSession(agent);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync("hi"));

        Assert.Equal("boom", exception.Message);
        Assert.Empty(session.Messages);
    }

    [Fact]
    public void Constructor_Throws_WhenAgentIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatSession(null!));
    }

    [Fact]
    public void Messages_IsEmpty_BeforeFirstSend()
    {
        var agent = new ChatClientAgent(new FakeChatClient("ok"));
        var session = new ChatSession(agent);

        Assert.Empty(session.Messages);
    }
}