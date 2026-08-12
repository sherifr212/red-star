using System.Diagnostics;
using System.Net;
using RedStar.Base;
using RedStar.Base.Telemetry;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class ModelsClientTests
{
    private static RedStarOptions WithBaseUrlAndApiKey(string baseUrl, string apiKey) =>
        new() { Agents = new AgentsOptions { Unsloth = new UnslothAgentOptions { BaseUrl = baseUrl, ApiKey = apiKey } } };

    [Fact]
    public async Task ListAsync_SendsNoAuthorizationHeader_WhenApiKeyIsEmpty()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"data": []}""");
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "");
        var client = new ModelsClient(new HttpClient(handler), options);

        await client.ListAsync();

        Assert.Null(handler.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task ListAsync_SendsBearerHeader_WhenApiKeyIsConfigured()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"data": []}""");
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "secret-key");
        var client = new ModelsClient(new HttpClient(handler), options);

        await client.ListAsync();

        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("secret-key", handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ListAsync_ParsesModelsFromResponseBody()
    {
        var json = """{"data": [{"id": "model-a", "loaded": true}, {"id": "model-b", "loaded": false}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "");
        var client = new ModelsClient(new HttpClient(handler), options);

        var models = await client.ListAsync();

        Assert.Equal(2, models.Count);
        Assert.Equal("model-a", models[0].Id);
        Assert.True(models[0].Loaded);
        Assert.Equal("model-b", models[1].Id);
        Assert.False(models[1].Loaded);
    }

    [Fact]
    public async Task ListAsync_RecordsModelIdsAndLoadedIds_InActivityTags()
    {
        var json = """{"data": [{"id": "model-a", "loaded": true}, {"id": "model-b", "loaded": false}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "");
        var client = new ModelsClient(new HttpClient(handler), options);

        Activity? capturedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RedStarTelemetry.ServiceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => capturedActivity = activity,
        };
        ActivitySource.AddActivityListener(listener);

        await client.ListAsync();

        Assert.NotNull(capturedActivity);
        Assert.Equal(2, capturedActivity!.GetTagItem("models.count"));
        Assert.Equal("model-a, model-b", capturedActivity.GetTagItem("models.ids"));
        Assert.Equal("model-a", capturedActivity.GetTagItem("models.loaded_ids"));
    }

    [Fact]
    public async Task ListAsync_ReturnsEmptyList_WhenResponseHasNoDataField()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{}");
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "");
        var client = new ModelsClient(new HttpClient(handler), options);

        var models = await client.ListAsync();

        Assert.Empty(models);
    }

    [Fact]
    public async Task ListAsync_Throws_OnNonSuccessStatusCode()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized, """{"detail": "no"}""");
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "");
        var client = new ModelsClient(new HttpClient(handler), options);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ListAsync());
    }

    [Theory]
    [InlineData("http://example.test/v1")]
    [InlineData("http://example.test/v1/")]
    public async Task ListAsync_RequestsModelsEndpoint_RegardlessOfBaseUrlTrailingSlash(string baseUrl)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"data": []}""");
        var options = WithBaseUrlAndApiKey(baseUrl, "");
        var client = new ModelsClient(new HttpClient(handler), options);

        await client.ListAsync();

        Assert.Equal("http://example.test/v1/models", handler.LastRequest?.RequestUri?.ToString());
    }
}
