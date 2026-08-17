using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RedStar.Base.Telemetry;

namespace RedStar.Base.Agents.GoogleAI;

public sealed class GoogleAIModelsClient : IModelsClient, IDisposable
{
    private readonly HttpClient _httpClient;

    public GoogleAIModelsClient(HttpClient httpClient, RedStarOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        var googleAI = options.Agents.GoogleAI;
        if (string.IsNullOrEmpty(googleAI.ApiKey))
        {
            throw new InvalidOperationException(
                "Google AI API key is required to list models. Set it via environment variable " +
                "RedStar__Agents__GoogleAI__ApiKey or appsettings.local.json.");
        }

        var baseUrl = googleAI.BaseUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^"/openai".Length];
        }

        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += '/';
        }

        baseUrl += "v1beta/";

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", googleAI.ApiKey);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = RedStarTelemetry.ActivitySource.StartActivity("GoogleAIModelsClient.ListAsync", ActivityKind.Client);
        var logger = RedStarTelemetry.CreateLogger("RedStar.Agents.GoogleAI.GoogleAIModelsClient");
        var stopwatch = Stopwatch.StartNew();
        var tags = new TagList { { "operation", "models" } };

        try
        {
            var response = await _httpClient.GetFromJsonAsync<GoogleModelsResponse>("models", cancellationToken);
            var models = response?.Models ?? [];

            var modelIds = string.Join(", ", models.Select(m => m.Name));
            activity?.SetTag("models.count", models.Count);
            activity?.SetTag("models.ids", modelIds);
            logger.LogModelsListed(models.Count, stopwatch.Elapsed.TotalMilliseconds, modelIds);

            var result = models.Select(m => new ModelInfo(
                Id: m.Name,
                Loaded: true,
                Type: null,
                MaxContextLength: null,
                Quantization: null
            )).ToList();

            return result;
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

    public void Dispose() => _httpClient.Dispose();

    private sealed record GoogleModelsResponse
    {
        [JsonPropertyName("models")]
        public List<GoogleModel> Models { get; set; } = [];
    }

    private sealed record GoogleModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }
}
