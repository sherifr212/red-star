using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RedStar.Base.Telemetry;

namespace RedStar.Base.Agents.LMStudio;

/// <summary>
/// Lists models via LM Studio's <em>native</em> <c>GET /api/v0/models</c> endpoint rather than the
/// OpenAI-compatible <c>/v1/models</c> that <see cref="ModelsClient"/> uses for Unsloth -- LM Studio's
/// standard-schema <c>/v1/models</c> carries no load-state field at all, while <c>/api/v0/models</c> reports
/// <c>state</c> (loaded/not-loaded), <c>type</c> (llm/vlm/embeddings), <c>max_context_length</c>, and
/// <c>quantization</c> per model, which is what <see cref="ModelInfo"/>/<see cref="ModelSelector"/> need.
/// </summary>
public sealed class LMStudioModelsClient : IModelsClient, IDisposable
{
    private readonly HttpClient _httpClient;

    /// <param name="handler">Custom transport, e.g. a fake for tests. Defaults to a real HTTP handler.</param>
    public LMStudioModelsClient(RedStarOptions options, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var lmStudio = options.Agents.LMStudio;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.BaseAddress = new Uri(EnsureTrailingSlash(ServerRoot(lmStudio.BaseUrl)));
        if (!string.IsNullOrEmpty(lmStudio.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", lmStudio.ApiKey);
        }
    }

    public async Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = RedStarTelemetry.ActivitySource.StartActivity("LMStudioModelsClient.ListAsync", ActivityKind.Client);
        var logger = RedStarTelemetry.CreateLogger("RedStar.LMStudioModelsClient");
        var stopwatch = Stopwatch.StartNew();
        var tags = new TagList { { "operation", "models" } };

        try
        {
            using var response = await _httpClient.GetAsync("api/v0/models", cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<LMStudioModelListResponse>(cancellationToken: cancellationToken);
            var models = (payload?.Data ?? []).Select(ToModelInfo).ToList();
            var modelIds = string.Join(", ", models.Select(m => m.Id));
            var loadedModelIds = string.Join(", ", models.Where(m => m.Loaded).Select(m => m.Id));

            activity?.SetTag("models.count", models.Count);
            activity?.SetTag("models.ids", modelIds);
            activity?.SetTag("models.loaded_ids", loadedModelIds);
            logger.LogInformation(
                "Listed {ModelCount} models in {ElapsedMs}ms: {ModelIds} (loaded: {LoadedModelIds})",
                models.Count, stopwatch.Elapsed.TotalMilliseconds, modelIds, loadedModelIds);

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

    private static ModelInfo ToModelInfo(LMStudioModelEntry entry) => new(
        entry.Id,
        string.Equals(entry.State, "loaded", StringComparison.OrdinalIgnoreCase),
        entry.Type,
        entry.MaxContextLength,
        entry.Quantization);

    /// <summary>
    /// LM Studio's native <c>/api/v0/*</c> endpoints hang off the server root (e.g.
    /// <c>http://127.0.0.1:1234/</c>), not the <c>/v1</c> OpenAI-compatible prefix that
    /// <see cref="LMStudioAgentOptions.BaseUrl"/> is configured with (same shape as Unsloth's BaseUrl, for
    /// consistency and so both endpoint families can be reached from one configured URL) -- strips a
    /// trailing <c>/v1</c> (with or without a trailing slash) so the result can be combined with
    /// <c>api/v0/models</c> above.
    /// </summary>
    private static string ServerRoot(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? trimmed[..^3] : trimmed;
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}

internal sealed record LMStudioModelListResponse(
    [property: JsonPropertyName("data")] List<LMStudioModelEntry> Data);

internal sealed record LMStudioModelEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("max_context_length")] int? MaxContextLength,
    [property: JsonPropertyName("quantization")] string? Quantization);
