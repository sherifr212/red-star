using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using RedStar.Base.Telemetry;

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
        using var activity = RedStarTelemetry.ActivitySource.StartActivity("ModelsClient.ListAsync", ActivityKind.Client);
        var logger = RedStarTelemetry.CreateLogger("RedStar.ModelsClient");
        var stopwatch = Stopwatch.StartNew();
        var tags = new TagList { { "operation", "models" } };

        try
        {
            using var response = await _httpClient.GetAsync("models", cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<ModelListResponse>(cancellationToken: cancellationToken);
            var models = payload?.Data ?? [];

            activity?.SetTag("models.count", models.Count);
            logger.LogInformation("Listed {ModelCount} models in {ElapsedMs}ms", models.Count, stopwatch.Elapsed.TotalMilliseconds);

            return models;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Listing models failed after {ElapsedMs}ms", stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        finally
        {
            RedStarTelemetry.RequestCount.Add(1, tags);
            RedStarTelemetry.RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}
