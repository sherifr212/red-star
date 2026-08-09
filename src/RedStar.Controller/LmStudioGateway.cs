using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace RedStar.Controller;

/// <summary>
/// Hand-rolled HttpClient gateway to a real LM Studio server, same shape as RedStar.Base's
/// ModelsClient (constructor takes an optional <see cref="HttpMessageHandler"/> so tests can
/// substitute a fake transport instead of hitting the network). Every call forwards the request body
/// verbatim and returns LM Studio's status code/body verbatim -- no DTO (de)serialization in either
/// direction, so this can never drop or mis-map a field LM Studio's schema adds later.
/// </summary>
public sealed class LmStudioGateway : ILmStudioGateway, IDisposable
{
    private readonly HttpClient _httpClient;

    public LmStudioGateway(IOptions<LmStudioOptions> options, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var lmStudio = options.Value;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.BaseAddress = new Uri(EnsureTrailingSlash(lmStudio.BaseUrl));
        if (!string.IsNullOrEmpty(lmStudio.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", lmStudio.ApiKey);
        }
    }

    public Task<LmStudioResponse> GetModelsAsync(CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "api/v1/models", body: null, cancellationToken);

    public Task<LmStudioResponse> LoadModelAsync(string requestBodyJson, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/v1/models/load", requestBodyJson, cancellationToken);

    public Task<LmStudioResponse> UnloadModelAsync(string requestBodyJson, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/v1/models/unload", requestBodyJson, cancellationToken);

    public Task<LmStudioResponse> DownloadModelAsync(string requestBodyJson, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "api/v1/models/download", requestBodyJson, cancellationToken);

    public Task<LmStudioResponse> GetDownloadStatusAsync(string jobId, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, $"api/v1/models/download/status/{Uri.EscapeDataString(jobId)}", body: null, cancellationToken);

    private async Task<LmStudioResponse> SendAsync(HttpMethod method, string relativeUrl, string? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        return new LmStudioResponse((int)response.StatusCode, responseBody);
    }

    public void Dispose() => _httpClient.Dispose();

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
