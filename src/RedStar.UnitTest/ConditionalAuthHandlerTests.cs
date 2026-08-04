using System.Net;
using System.Net.Http.Headers;
using RedStar.Base;
using RedStar.UnitTest.Fakes;

namespace RedStar.UnitTest;

public class ConditionalAuthHandlerTests
{
    [Fact]
    public async Task SendAsync_RemovesAuthorizationHeader_WhenStripEnabled()
    {
        var inner = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var handler = new ConditionalAuthHandler(stripAuthHeader: true, innerHandler: inner);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "should-be-removed");

        await client.SendAsync(request);

        Assert.Null(inner.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_PreservesAuthorizationHeader_WhenStripDisabled()
    {
        var inner = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var handler = new ConditionalAuthHandler(stripAuthHeader: false, innerHandler: inner);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "keep-me");

        await client.SendAsync(request);

        Assert.Equal("keep-me", inner.LastRequest?.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task SendAsync_StripEnabled_IsHarmless_WhenNoAuthorizationHeaderWasSet()
    {
        var inner = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var handler = new ConditionalAuthHandler(stripAuthHeader: true, innerHandler: inner);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.test/v1/models");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(inner.LastRequest?.Headers.Authorization);
    }
}
