namespace RedStar.Base.Agents.LMStudio;

/// <summary>
/// Typed <see cref="HttpClient"/> wrapper for the LM Studio agent -- see <c>UnslothHttpClient</c>'s
/// remarks for the rationale (typed clients over a string-named <see cref="IHttpClientFactory"/> client).
/// <see cref="ConditionalAuthHandler"/> is still attached to this type's registration in
/// <c>Program.cs</c>, unchanged from the untyped client it replaces.
/// </summary>
public sealed class LMStudioHttpClient
{
    public HttpClient HttpClient { get; }

    public LMStudioHttpClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        HttpClient = httpClient;
    }
}