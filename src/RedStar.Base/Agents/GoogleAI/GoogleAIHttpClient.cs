namespace RedStar.Base.Agents.GoogleAI;

/// <summary>
/// Typed <see cref="HttpClient"/> wrapper for the GoogleAI agent -- see <c>UnslothHttpClient</c>'s
/// remarks for the rationale (typed clients over a string-named <see cref="IHttpClientFactory"/> client).
/// No message handler is attached to this type's registration in <c>Program.cs</c>, same as the untyped
/// client it replaces -- Gemini always requires a real API key set directly by the <c>Google.GenAI</c>
/// SDK, so there's no <c>ConditionalAuthHandler</c> equivalent here.
/// </summary>
public sealed class GoogleAIHttpClient
{
    public HttpClient HttpClient { get; }

    public GoogleAIHttpClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        HttpClient = httpClient;
    }
}