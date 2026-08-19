using System.Net;
using System.Text;

namespace RedStar.UnitTest.Controller.Fakes;

/// <summary>
/// Local copy of RedStar.UnitTest/Fakes/FakeHttpMessageHandler.cs -- duplicated rather than shared
/// because this test project intentionally references only RedStar.Controller, not RedStar.UnitTest
/// or RedStar.Base (see CLAUDE.md test-project-per-project convention: RedStar.UnitTest.Cli mirrors
/// this for RedStar.Cli).
/// </summary>
internal sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string? jsonContent = null) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        var response = new HttpResponseMessage(statusCode);
        if (jsonContent is not null)
        {
            response.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        }

        return response;
    }
}