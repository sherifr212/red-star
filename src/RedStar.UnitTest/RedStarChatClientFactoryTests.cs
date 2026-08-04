using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using RedStar.Base;

namespace RedStar.UnitTest;

public class RedStarChatClientFactoryTests
{
    [Fact]
    public void CreateChatOptions_ReturnsNull_WhenWebSearchDisabled()
    {
        var options = new RedStarOptions { WebSearchEnabled = false };

        var chatOptions = RedStarChatClientFactory.CreateChatOptions(options);

        Assert.Null(chatOptions);
    }

    [Fact]
    public void CreateChatOptions_ReturnsOptionsWithRawRepresentationFactory_WhenWebSearchEnabled()
    {
        var options = new RedStarOptions { WebSearchEnabled = true };

        var chatOptions = RedStarChatClientFactory.CreateChatOptions(options);

        Assert.NotNull(chatOptions);
        Assert.NotNull(chatOptions!.RawRepresentationFactory);
    }

    [Fact]
    public void CreateChatOptions_RawRepresentation_IsChatCompletionOptionsWithOnlyWebSearchEnabled()
    {
        var options = new RedStarOptions { WebSearchEnabled = true };
        var chatOptions = RedStarChatClientFactory.CreateChatOptions(options);

        var raw = chatOptions!.RawRepresentationFactory!(null!);
        var completionOptions = Assert.IsType<ChatCompletionOptions>(raw);

        var json = ModelReaderWriter.Write(completionOptions, ModelReaderWriterOptions.Json).ToString();

        Assert.Contains("\"enable_tools\":true", json);
        Assert.Contains("\"enabled_tools\":[\"web_search\"]", json);
    }

    [Fact]
    public void CreateChatOptions_Throws_WhenOptionsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => RedStarChatClientFactory.CreateChatOptions(null!));
    }
}
