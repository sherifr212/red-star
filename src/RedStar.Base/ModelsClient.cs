using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.Extensions.Logging;

using RedStar.Base.Telemetry;

namespace RedStar.Base;

public sealed class ModelsClient : IModelsClient
{
    private readonly HttpClient _httpClient;

    /// <param name="httpClient">Transport to use. Caller owns its construction/lifetime.</param>
    public ModelsClient(HttpClient httpClient, RedStarOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        var unsloth = options.Agents.Unsloth;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(EnsureTrailingSlash(unsloth.BaseUrl));
        if (!string.IsNullOrEmpty(unsloth.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", unsloth.ApiKey);
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
            var modelIds = string.Join(", ", models.Select(m => m.Id));
            var loadedModelIds = string.Join(", ", models.Where(m => m.Loaded).Select(m => m.Id));

            activity?.SetTag("models.count", models.Count);
            activity?.SetTag("models.ids", modelIds);
            activity?.SetTag("models.loaded_ids", loadedModelIds);
            logger.LogModelsListedWithLoaded(models.Count, stopwatch.Elapsed.TotalMilliseconds, modelIds, loadedModelIds);

            return models;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogModelsListFailed(ex, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }
        finally
        {
            RedStarTelemetry.RequestCount.Add(1, tags);
            RedStarTelemetry.RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
        }
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}