using System.Text.Json.Serialization;

namespace RedStar.Base;

public sealed record ModelInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("loaded")] bool Loaded);

internal sealed record ModelListResponse(
    [property: JsonPropertyName("data")] List<ModelInfo> Data);
