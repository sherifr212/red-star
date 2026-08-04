using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace RedStar.Base;

public sealed class ModelsClient : IModelsClient, IDisposable
{
    private readonly HttpClient _httpClient;

    /// <param name="handler">Custom transport, e.g. a fake for tests. Defaults to a real HTTP handler.</param>
    public ModelsClient(RedStarOptions options, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        }
    }

    public async Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("models", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ModelListResponse>(cancellationToken: cancellationToken);
        return payload?.Data ?? [];
    }

    public void Dispose() => _httpClient.Dispose();

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
