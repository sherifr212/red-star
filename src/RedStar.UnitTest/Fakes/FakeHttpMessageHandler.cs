using System.Net;
using System.Text;

namespace RedStar.UnitTest.Fakes;

internal sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string? jsonContent = null) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;

        var response = new HttpResponseMessage(statusCode);
        if (jsonContent is not null)
        {
            response.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        }

        return Task.FromResult(response);
    }
}
