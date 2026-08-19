namespace RedStar.Base.Agents.Unsloth;

/// <summary>
/// Typed <see cref="HttpClient"/> wrapper for the Unsloth agent, registered via
/// <c>IServiceCollection.AddHttpClient&lt;UnslothHttpClient&gt;()</c> (see <c>Program.cs</c>) instead of a
/// string-named client resolved through <see cref="IHttpClientFactory"/> -- this is the recommended
/// typed-client pattern (https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory#typed-clients),
/// which gives each agent's client its own DI-resolvable type instead of a magic string
/// (<c>AgentNames.Unsloth</c>) that has to match between registration and every call site.
/// <see cref="ConditionalAuthHandler"/> is still attached to this type's registration in
/// <c>Program.cs</c>, unchanged from the untyped client it replaces.
/// </summary>
public sealed class UnslothHttpClient
{
    public HttpClient HttpClient { get; }

    public UnslothHttpClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        HttpClient = httpClient;
    }
}