namespace RedStar.Base;

/// <summary>
/// Strips the Authorization header from outgoing requests when no API key is configured,
/// instead of sending a placeholder Bearer token. The OpenAI SDK's client always requires a
/// non-empty credential, so this is what makes a genuine "no auth" request possible.
/// </summary>
public sealed class ConditionalAuthHandler : DelegatingHandler
{
    private readonly bool _stripAuthHeader;

    public ConditionalAuthHandler(bool stripAuthHeader, HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler())
    {
        _stripAuthHeader = stripAuthHeader;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_stripAuthHeader)
        {
            request.Headers.Authorization = null;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
