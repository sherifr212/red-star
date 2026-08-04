using System.Net;
using RedStar.Base;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class ModelsClientTests
{
    [Fact]
    public async Task ListAsync_SendsNoAuthorizationHeader_WhenApiKeyIsEmpty()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"data": []}""");
        var options = new RedStarOptions { BaseUrl = "http://example.test/v1", ApiKey = "" };
        using var client = new ModelsClient(options, handler);

        await client.ListAsync();

        Assert.Null(handler.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task ListAsync_SendsBearerHeader_WhenApiKeyIsConfigured()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"data": []}""");
        var options = new RedStarOptions { BaseUrl = "http://example.test/v1", ApiKey = "secret-key" };
        using var client = new ModelsClient(options, handler);

        await client.ListAsync();

        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("secret-key", handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ListAsync_ParsesModelsFromResponseBody()
    {
        var json = """{"data": [{"id": "model-a", "loaded": true}, {"id": "model-b", "loaded": false}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var options = new RedStarOptions { BaseUrl = "http://example.test/v1", ApiKey = "" };
        using var client = new ModelsClient(options, handler);

        var models = await client.ListAsync();

        Assert.Equal(2, models.Count);
        Assert.Equal("model-a", models[0].Id);
        Assert.True(models[0].Loaded);
        Assert.Equal("model-b", models[1].Id);
        Assert.False(models[1].Loaded);
    }

    [Fact]
    public async Task ListAsync_ReturnsEmptyList_WhenResponseHasNoDataField()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var options = new RedStarOptions { BaseUrl = "http://example.test/v1", ApiKey = "" };
        using var client = new ModelsClient(options, handler);

        var models = await client.ListAsync();

        Assert.Empty(models);
    }

    [Fact]
    public async Task ListAsync_Throws_OnNonSuccessStatusCode()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized, """{"detail": "no"}""");
        var options = new RedStarOptions { BaseUrl = "http://example.test/v1", ApiKey = "" };
        using var client = new ModelsClient(options, handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ListAsync());
    }

    [Theory]
    [InlineData("http://example.test/v1")]
    [InlineData("http://example.test/v1/")]
    public async Task ListAsync_RequestsModelsEndpoint_RegardlessOfBaseUrlTrailingSlash(string baseUrl)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"data": []}""");
        var options = new RedStarOptions { BaseUrl = baseUrl, ApiKey = "" };
        using var client = new ModelsClient(options, handler);

        await client.ListAsync();

        Assert.Equal("http://example.test/v1/models", handler.LastRequest?.RequestUri?.ToString());
    }
}
