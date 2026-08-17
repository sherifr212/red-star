using System.Net;
using System.Net.Http.Headers;
using RedStar.Base;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class ConditionalAuthHandlerTests
{
    [Fact]
    public async Task SendAsync_RemovesAuthorizationHeader_WhenCredentialIsPlaceholder()
    {
        var inner = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var handler = new ConditionalAuthHandler { InnerHandler = inner };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ConditionalAuthHandler.NoAuthPlaceholder);

        await client.SendAsync(request);

        Assert.Null(inner.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_PreservesAuthorizationHeader_WhenCredentialIsReal()
    {
        var inner = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var handler = new ConditionalAuthHandler { InnerHandler = inner };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "keep-me");

        await client.SendAsync(request);

        Assert.Equal("keep-me", inner.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendAsync_IsHarmless_WhenNoAuthorizationHeaderWasSet()
    {
        var inner = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var handler = new ConditionalAuthHandler { InnerHandler = inner };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/v1/models");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(inner.LastRequest?.Headers.Authorization);
    }
}
