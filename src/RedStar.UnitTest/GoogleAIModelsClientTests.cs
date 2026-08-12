using System.Net;
using RedStar.Base;
using RedStar.Base.Agents.GoogleAI;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class GoogleAIModelsClientTests
{
    private static RedStarOptions WithBaseUrlAndApiKey(string baseUrl, string apiKey) =>
        new() { Agents = new AgentsOptions { GoogleAI = new GoogleAIAgentOptions { BaseUrl = baseUrl, ApiKey = apiKey } } };

    [Fact]
    public void Constructor_Throws_WhenHttpClientIsNull()
    {
        var options = WithBaseUrlAndApiKey("https://generativelanguage.googleapis.com/openai/", "test-key");

        Assert.Throws<ArgumentNullException>(() => new GoogleAIModelsClient(null!, options));
    }

    [Fact]
    public void Constructor_Throws_WhenApiKeyIsEmpty()
    {
        var httpClient = new HttpClient();
        var options = WithBaseUrlAndApiKey("https://generativelanguage.googleapis.com/openai/", "");

        Assert.Throws<InvalidOperationException>(() => new GoogleAIModelsClient(httpClient, options));
    }

    [Fact]
    public async Task ListAsync_SendsApiKeyInHeader()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models": []}""");
        var httpClient = new HttpClient(handler);
        var options = WithBaseUrlAndApiKey("https://generativelanguage.googleapis.com/openai/", "test-key");
        using var client = new GoogleAIModelsClient(httpClient, options);

        await client.ListAsync();

        Assert.Equal("test-key", handler.LastRequest?.Headers.GetValues("x-goog-api-key").FirstOrDefault());
    }

    [Fact]
    public async Task ListAsync_ReturnsModelsFromResponse()
    {
        var json = """
            {"models": [
              {"name": "gemini-1.5-pro", "displayName": "Gemini 1.5 Pro", "version": "001"},
              {"name": "gemini-1.5-flash", "displayName": "Gemini 1.5 Flash", "version": "001"}
            ]}
            """;
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var httpClient = new HttpClient(handler);
        var options = WithBaseUrlAndApiKey("https://generativelanguage.googleapis.com/openai/", "test-key");
        using var client = new GoogleAIModelsClient(httpClient, options);

        var models = await client.ListAsync();

        Assert.Equal(2, models.Count);
        Assert.Equal("gemini-1.5-pro", models[0].Id);
        Assert.True(models[0].Loaded);
        Assert.Null(models[0].Type);
        Assert.Equal("gemini-1.5-flash", models[1].Id);
    }

    [Fact]
    public async Task ListAsync_ReturnsEmptyList_WhenResponseHasNoModels()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var httpClient = new HttpClient(handler);
        var options = WithBaseUrlAndApiKey("https://generativelanguage.googleapis.com/openai/", "test-key");
        using var client = new GoogleAIModelsClient(httpClient, options);

        var models = await client.ListAsync();

        Assert.Empty(models);
    }

    [Fact]
    public async Task ListAsync_Throws_OnNonSuccessStatusCode()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized, """{"error": "invalid key"}""");
        var httpClient = new HttpClient(handler);
        var options = WithBaseUrlAndApiKey("https://generativelanguage.googleapis.com/openai/", "bad-key");
        using var client = new GoogleAIModelsClient(httpClient, options);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ListAsync());
    }

    [Theory]
    [InlineData("https://generativelanguage.googleapis.com/openai", "https://generativelanguage.googleapis.com/v1beta/")]
    [InlineData("https://generativelanguage.googleapis.com/openai/", "https://generativelanguage.googleapis.com/v1beta/")]
    [InlineData("https://generativelanguage.googleapis.com", "https://generativelanguage.googleapis.com/v1beta/")]
    public async Task Constructor_BuildsCorrectModelsEndpointUrl(string baseUrl, string expectedModelsUrl)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models": []}""");
        var httpClient = new HttpClient(handler);
        var options = WithBaseUrlAndApiKey(baseUrl, "test-key");
        using var client = new GoogleAIModelsClient(httpClient, options);

        await client.ListAsync();

        var requestUrl = handler.LastRequest?.RequestUri?.ToString();
        Assert.NotNull(requestUrl);
        Assert.StartsWith(expectedModelsUrl, requestUrl);
    }
}
