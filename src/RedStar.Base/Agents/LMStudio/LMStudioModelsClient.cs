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
public sealed class LMStudioModelsClient : IModelsClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _modelsUri;
    private readonly string? _apiKey;

    /// <param name="httpClient">
    /// Transport to use. Caller owns its construction/lifetime -- this constructor deliberately never
    /// touches <see cref="HttpClient.BaseAddress"/>/<see cref="HttpClient.DefaultRequestHeaders"/> (see
    /// <c>ModelsClient</c>'s constructor remarks for why: callers may hand in a typed client's shared
    /// instance that's also reused as the chat agent's transport). Every field this client needs is
    /// instead applied per-request, in <see cref="ListAsync"/>.
    /// </param>
    public LMStudioModelsClient(HttpClient httpClient, RedStarOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        var lmStudio = options.Agents.LMStudio;
        _httpClient = httpClient;
        _modelsUri = new Uri(new Uri(EnsureTrailingSlash(ServerRoot(lmStudio.BaseUrl))), "api/v1/models");
        _apiKey = string.IsNullOrEmpty(lmStudio.ApiKey) ? null : lmStudio.ApiKey;
    }

    public async Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = RedStarTelemetry.ActivitySource.StartActivity("LMStudioModelsClient.ListAsync", ActivityKind.Client);
        var logger = RedStarTelemetry.CreateLogger("RedStar.LMStudioModelsClient");
        var stopwatch = Stopwatch.StartNew();
        var tags = new TagList { { "operation", "models" } };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _modelsUri);
            if (_apiKey is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var payload = System.Text.Json.JsonSerializer.Deserialize<LMStudioModelListResponse>(rawJson);

            var models = (payload?.Models ?? []).Select(ToModelInfo).ToList();
            var modelIds = string.Join(", ", models.Select(m => m.Id));
            var loadedModelIds = string.Join(", ", models.Where(m => m.Loaded).Select(m => m.Id));

            activity?.SetTag("models.count", models.Count);
            activity?.SetTag("models.ids", modelIds);
            activity?.SetTag("models.loaded_ids", loadedModelIds);
            activity?.SetTag("models.raw_json", rawJson);
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

    private static ModelInfo ToModelInfo(LMStudioModelEntry entry) => new LMStudioModelInfo(
        entry.Id,
        entry.LoadedInstances != null && entry.LoadedInstances.Count > 0,
        entry.Type,
        entry.MaxContextLength,
        entry.Quantization?.Name,
        entry.Publisher,
        entry.Architecture,
        entry.SizeBytes,
        entry.ParamsString,
        entry.Format,
        entry.Capabilities?.Vision,
        entry.Capabilities?.TrainedForToolUse);

    /// <summary>
    /// LM Studio's native <c>/api/v1/*</c> endpoints hang off the server root (e.g.
    /// <c>http://127.0.0.1:1234/</c>), not the <c>/v1</c> OpenAI-compatible prefix that
    /// <see cref="LMStudioAgentOptions.BaseUrl"/> is configured with (same shape as Unsloth's BaseUrl, for
    /// consistency and so both endpoint families can be reached from one configured URL) -- strips a
    /// trailing <c>/v1</c> (with or without a trailing slash) so the result can be combined with
    /// <c>api/v1/models</c> above.
    /// </summary>
    private static string ServerRoot(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');
        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? trimmed[..^3] : trimmed;
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}

internal sealed record LMStudioModelListResponse(
    [property: JsonPropertyName("models")] List<LMStudioModelEntry> Models);

internal sealed record LMStudioModelEntry(
    [property: JsonPropertyName("key")] string Id,
    [property: JsonPropertyName("loaded_instances")] List<LMStudioLoadedInstance>? LoadedInstances,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("max_context_length")] int? MaxContextLength,
    [property: JsonPropertyName("quantization")] LMStudioQuantization? Quantization,
    [property: JsonPropertyName("publisher")] string? Publisher,
    [property: JsonPropertyName("architecture")] string? Architecture,
    [property: JsonPropertyName("size_bytes")] long? SizeBytes,
    [property: JsonPropertyName("params_string")] string? ParamsString,
    [property: JsonPropertyName("format")] string? Format,
    [property: JsonPropertyName("capabilities")] LMStudioCapabilities? Capabilities);

internal sealed record LMStudioCapabilities(
    [property: JsonPropertyName("vision")] bool? Vision,
    [property: JsonPropertyName("trained_for_tool_use")] bool? TrainedForToolUse);

internal sealed record LMStudioLoadedInstance(
    [property: JsonPropertyName("id")] string Id);

internal sealed record LMStudioQuantization(
    [property: JsonPropertyName("name")] string? Name);

public sealed record LMStudioModelInfo : ModelInfo
{
    public string? Publisher { get; }
    public string? Architecture { get; }
    public long? SizeBytes { get; }
    public string? ParamsString { get; }
    public string? Format { get; }
    public bool? SupportsVision { get; }
    public bool? TrainedForToolUse { get; }

    public LMStudioModelInfo(
        string id,
        bool loaded,
        string? type,
        int? maxContextLength,
        string? quantization,
        string? publisher,
        string? architecture,
        long? sizeBytes,
        string? paramsString,
        string? format,
        bool? supportsVision,
        bool? trainedForToolUse)
        : base(id, loaded, type, maxContextLength, quantization)
    {
        Publisher = publisher;
        Architecture = architecture;
        SizeBytes = sizeBytes;
        ParamsString = paramsString;
        Format = format;
        SupportsVision = supportsVision;
        TrainedForToolUse = trainedForToolUse;
    }
}