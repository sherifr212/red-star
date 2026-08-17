using System.Text.Json.Serialization;

namespace RedStar.Base;

/// <summary>
/// One model the server knows about. <paramref name="Type"/>/<paramref name="MaxContextLength"/>/
/// <paramref name="Quantization"/> are populated by <see cref="RedStar.Base.Agents.LMStudio.LMStudioModelsClient"/>
/// (whose native <c>/api/v0/models</c> endpoint reports them) and always null from Unsloth's
/// <see cref="ModelsClient"/> (whose <c>/v1/models</c> response has no equivalent fields) -- callers that
/// care about them (see <see cref="ModelSelector"/>'s embeddings-model filtering) must treat null as
/// "unknown/not applicable", not "definitely not an embeddings model".
/// </summary>
public record ModelInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("loaded")] bool Loaded,
    [property: JsonPropertyName("type")] string? Type = null,
    [property: JsonPropertyName("max_context_length")] int? MaxContextLength = null,
    [property: JsonPropertyName("quantization")] string? Quantization = null);

internal sealed record ModelListResponse(
    [property: JsonPropertyName("data")] List<ModelInfo> Data);
