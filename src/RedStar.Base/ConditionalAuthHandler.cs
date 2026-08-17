namespace RedStar.Base;

/// <summary>
/// Strips the Authorization header from outgoing requests when no real API key is configured,
/// instead of sending <see cref="NoAuthPlaceholder"/> as a Bearer token. The OpenAI SDK's client
/// always requires a non-empty credential, so agent factories (<c>UnslothAgentFactory.Create</c>,
/// <c>LMStudioAgentFactory.Create</c>) pass <see cref="NoAuthPlaceholder"/> as that credential when
/// no real key is configured; this handler recognizes that exact value and strips the header it
/// produces, making a genuine "no auth" request possible. Stateless by design (no per-instance
/// config) so it can be registered once via <c>IServiceCollection.AddHttpClient(name)
/// .AddHttpMessageHandler(...)</c> and reused by every request through that named client, rather
/// than requiring a hand-built <see cref="HttpClient"/> per run.
/// </summary>
public sealed class ConditionalAuthHandler : DelegatingHandler
{
    /// <summary>
    /// The placeholder <see cref="System.ClientModel.ApiKeyCredential"/> value agent factories pass
    /// to the OpenAI SDK when no real API key is configured (the SDK rejects a null/empty credential).
    /// </summary>
    public const string NoAuthPlaceholder = "not-needed";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.Headers.Authorization?.Parameter, NoAuthPlaceholder, StringComparison.Ordinal))
        {
            request.Headers.Authorization = null;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
