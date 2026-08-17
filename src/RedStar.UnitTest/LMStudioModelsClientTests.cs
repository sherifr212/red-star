using System.Diagnostics;
using System.Net;
using RedStar.Base;
using RedStar.Base.Agents.LMStudio;
using RedStar.Base.Telemetry;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class LMStudioModelsClientTests
{
    private static RedStarOptions WithBaseUrlAndApiKey(string baseUrl, string apiKey) =>
        new() { Agents = new AgentsOptions { LMStudio = new LMStudioAgentOptions { BaseUrl = baseUrl, ApiKey = apiKey } } };

    [Fact]
    public async Task ListAsync_SendsNoAuthorizationHeader_WhenApiKeyIsEmpty()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models": []}""");
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "");
        var client = new LMStudioModelsClient(new HttpClient(handler), options);

        await client.ListAsync();

        Assert.Null(handler.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task ListAsync_SendsBearerHeader_WhenApiKeyIsConfigured()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models": []}""");
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "secret-key");
        var client = new LMStudioModelsClient(new HttpClient(handler), options);

        await client.ListAsync();

        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("secret-key", handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ListAsync_ParsesStateTypeContextAndQuantization_FromResponseBody()
    {
        var json = """
            {"models": [
              {"key": "model-a", "loaded_instances": [{"id": "model-a"}], "type": "llm", "max_context_length": 32768, "quantization": {"name": "Q4_K_M"}},
              {"key": "model-b", "loaded_instances": [], "type": "embeddings", "max_context_length": 512, "quantization": {"name": "F16"}}
            ]}
            """;
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "");
        var client = new LMStudioModelsClient(new HttpClient(handler), options);

        var models = await client.ListAsync();

        Assert.Equal(2, models.Count);
        Assert.Equal("model-a", models[0].Id);
        Assert.True(models[0].Loaded);
        Assert.Equal("llm", models[0].Type);
        Assert.Equal(32768, models[0].MaxContextLength);
        Assert.Equal("Q4_K_M", models[0].Quantization);
        Assert.Equal("model-b", models[1].Id);
        Assert.False(models[1].Loaded);
        Assert.Equal("embeddings", models[1].Type);
    }

    [Fact]
    public async Task ListAsync_RecordsModelIdsAndLoadedIds_InActivityTags()
    {
        var json = """{"models": [{"key": "model-a", "loaded_instances": [{"id": "model-a"}]}, {"key": "model-b", "loaded_instances": []}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "");
        var client = new LMStudioModelsClient(new HttpClient(handler), options);

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
        var client = new LMStudioModelsClient(new HttpClient(handler), options);

        var models = await client.ListAsync();

        Assert.Empty(models);
    }

    [Fact]
    public async Task ListAsync_Throws_OnNonSuccessStatusCode()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized, """{"detail": "no"}""");
        var options = WithBaseUrlAndApiKey("http://example.test/v1", "");
        var client = new LMStudioModelsClient(new HttpClient(handler), options);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ListAsync());
    }

    [Theory]
    [InlineData("http://example.test/v1", "http://example.test/api/v1/models")]
    [InlineData("http://example.test/v1/", "http://example.test/api/v1/models")]
    [InlineData("http://example.test", "http://example.test/api/v1/models")]
    [InlineData("http://example.test/", "http://example.test/api/v1/models")]
    public async Task ListAsync_RequestsNativeApiV1ModelsEndpoint_DerivedFromBaseUrl(string baseUrl, string expectedUrl)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models": []}""");
        var options = WithBaseUrlAndApiKey(baseUrl, "");
        var client = new LMStudioModelsClient(new HttpClient(handler), options);

        await client.ListAsync();

        Assert.Equal(expectedUrl, handler.LastRequest?.RequestUri?.ToString());
    }
}
