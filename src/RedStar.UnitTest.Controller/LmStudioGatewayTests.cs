using System.Net;
using Microsoft.Extensions.Options;
using RedStar.Controller;
using RedStar.UnitTest.Controller.Fakes;

namespace RedStar.UnitTest.Controller;

public class LmStudioGatewayTests
{
    private static IOptions<LmStudioOptions> WithBaseUrlAndApiKey(string baseUrl, string apiKey) =>
        Options.Create(new LmStudioOptions { BaseUrl = baseUrl, ApiKey = apiKey });

    [Fact]
    public async Task GetModelsAsync_SendsNoAuthorizationHeader_WhenApiKeyIsEmpty()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models": []}""");
        using var gateway = new LmStudioGateway(WithBaseUrlAndApiKey("http://example.test", ""), handler);

        await gateway.GetModelsAsync();

        Assert.Null(handler.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task GetModelsAsync_SendsBearerHeader_WhenApiKeyIsConfigured()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models": []}""");
        using var gateway = new LmStudioGateway(WithBaseUrlAndApiKey("http://example.test", "secret-key"), handler);

        await gateway.GetModelsAsync();

        Assert.Equal("Bearer", handler.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("secret-key", handler.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Theory]
    [InlineData("http://example.test")]
    [InlineData("http://example.test/")]
    public async Task GetModelsAsync_RequestsModelsEndpoint_RegardlessOfBaseUrlTrailingSlash(string baseUrl)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"models": []}""");
        using var gateway = new LmStudioGateway(WithBaseUrlAndApiKey(baseUrl, ""), handler);

        await gateway.GetModelsAsync();

        Assert.Equal("http://example.test/api/v1/models", handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Get, handler.LastRequest?.Method);
    }

    [Fact]
    public async Task GetModelsAsync_ReturnsStatusCodeAndBodyVerbatim()
    {
        var json = """{"models": [{"key": "some/model"}]}""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, json);
        using var gateway = new LmStudioGateway(WithBaseUrlAndApiKey("http://example.test", ""), handler);

        var response = await gateway.GetModelsAsync();

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(json, response.Body);
    }

    [Fact]
    public async Task GetModelsAsync_ReturnsNonSuccessStatusCodeVerbatim_WithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Unauthorized, """{"error": "no token"}""");
        using var gateway = new LmStudioGateway(WithBaseUrlAndApiKey("http://example.test", ""), handler);

        var response = await gateway.GetModelsAsync();

        Assert.Equal(401, response.StatusCode);
        Assert.Equal("""{"error": "no token"}""", response.Body);
    }

    [Fact]
    public async Task LoadModelAsync_PostsToLoadEndpoint_ForwardingRequestBodyVerbatim()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"status": "loaded"}""");
        using var gateway = new LmStudioGateway(WithBaseUrlAndApiKey("http://example.test", ""), handler);
        var requestBody = """{"model": "openai/gpt-oss-20b", "context_length": 8000}""";

        var response = await gateway.LoadModelAsync(requestBody);

        Assert.Equal("http://example.test/api/v1/models/load", handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.Equal(requestBody, handler.LastRequestBody);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("""{"status": "loaded"}""", response.Body);
    }

    [Fact]
    public async Task UnloadModelAsync_PostsToUnloadEndpoint_ForwardingRequestBodyVerbatim()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"instance_id": "openai/gpt-oss-20b"}""");
        using var gateway = new LmStudioGateway(WithBaseUrlAndApiKey("http://example.test", ""), handler);
        var requestBody = """{"instance_id": "openai/gpt-oss-20b"}""";

        await gateway.UnloadModelAsync(requestBody);

        Assert.Equal("http://example.test/api/v1/models/unload", handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.Equal(requestBody, handler.LastRequestBody);
    }

    [Fact]
    public async Task DownloadModelAsync_PostsToDownloadEndpoint_ForwardingRequestBodyVerbatim()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"job_id": "job_1"}""");
        using var gateway = new LmStudioGateway(WithBaseUrlAndApiKey("http://example.test", ""), handler);
        var requestBody = """{"model": "ibm/granite-4-micro"}""";

        await gateway.DownloadModelAsync(requestBody);

        Assert.Equal("http://example.test/api/v1/models/download", handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest?.Method);
        Assert.Equal(requestBody, handler.LastRequestBody);
    }

    [Fact]
    public async Task GetDownloadStatusAsync_RequestsDownloadStatusEndpoint_WithJobIdInPath()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"status": "downloading"}""");
        using var gateway = new LmStudioGateway(WithBaseUrlAndApiKey("http://example.test", ""), handler);

        await gateway.GetDownloadStatusAsync("job_493c7c9ded");

        Assert.Equal("http://example.test/api/v1/models/download/status/job_493c7c9ded", handler.LastRequest?.RequestUri?.ToString());
        Assert.Equal(HttpMethod.Get, handler.LastRequest?.Method);
    }

    [Fact]
    public async Task GetDownloadStatusAsync_EscapesJobIdInPath()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"status": "downloading"}""");
        using var gateway = new LmStudioGateway(WithBaseUrlAndApiKey("http://example.test", ""), handler);

        await gateway.GetDownloadStatusAsync("job/with spaces");

        Assert.Equal(
            "http://example.test/api/v1/models/download/status/job%2Fwith%20spaces",
            handler.LastRequest?.RequestUri?.AbsoluteUri);
    }
}
