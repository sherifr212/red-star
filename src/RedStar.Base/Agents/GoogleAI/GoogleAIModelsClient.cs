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
    private readonly Uri _modelsUri;
    private readonly string _apiKey;

    /// <param name="httpClient">
    /// Transport to use. Caller owns its construction/lifetime -- this constructor deliberately never
    /// touches <see cref="HttpClient.BaseAddress"/>/<see cref="HttpClient.DefaultRequestHeaders"/> (see
    /// <c>ModelsClient</c>'s constructor remarks for the general rationale). This matters especially here:
    /// <c>GoogleAIHttpClient</c>'s shared instance is also handed to <c>GoogleAIAgentFactory.Create</c>,
    /// whose <c>Google.GenAI</c> SDK sets its own <c>x-goog-api-key</c> header on every request it sends --
    /// adding a second, client-wide default of the same header here would duplicate it on those requests
    /// too. Every field this client needs is instead applied per-request, in <see cref="ListAsync"/>.
    /// </param>
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
        _modelsUri = new Uri(new Uri(baseUrl), "models");
        _apiKey = googleAI.ApiKey;
    }

    public async Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var activity = RedStarTelemetry.ActivitySource.StartActivity("GoogleAIModelsClient.ListAsync", ActivityKind.Client);
        var logger = RedStarTelemetry.CreateLogger("RedStar.Agents.GoogleAI.GoogleAIModelsClient");
        var stopwatch = Stopwatch.StartNew();
        var tags = new TagList { { "operation", "models" } };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _modelsUri);
            request.Headers.Add("x-goog-api-key", _apiKey);

            using var httpResponse = await _httpClient.SendAsync(request, cancellationToken);
            httpResponse.EnsureSuccessStatusCode();

            var response = await httpResponse.Content.ReadFromJsonAsync<GoogleModelsResponse>(cancellationToken: cancellationToken);
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