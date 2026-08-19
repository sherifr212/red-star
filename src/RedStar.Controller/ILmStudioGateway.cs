namespace RedStar.Controller;

/// <summary>Raw passthrough result of a proxied LM Studio call: the exact status code and JSON body LM Studio returned.</summary>
public readonly record struct LmStudioResponse(int StatusCode, string Body);

/// <summary>
/// Proxies LM Studio's native v1 REST API (model listing/loading/unloading/downloading -- everything
/// except <c>POST /api/v1/chat</c>, which is inference and out of scope here; a specialized agent owns
/// initiating and consuming that stream). Every method forwards the request/response body verbatim so
/// this gateway stays byte-for-byte faithful to whatever LM Studio's API returns, including fields
/// added to LM Studio's schema after this was written.
/// </summary>
public interface ILmStudioGateway
{
    /// <summary>GET /api/v1/models -- list all loaded and downloaded models.</summary>
    Task<LmStudioResponse> GetModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>POST /api/v1/models/load -- load a model (JIT-loads it if not already loaded). <paramref name="requestBodyJson"/> is forwarded verbatim.</summary>
    Task<LmStudioResponse> LoadModelAsync(string requestBodyJson, CancellationToken cancellationToken = default);

    /// <summary>POST /api/v1/models/unload -- unload a loaded model instance. <paramref name="requestBodyJson"/> is forwarded verbatim.</summary>
    Task<LmStudioResponse> UnloadModelAsync(string requestBodyJson, CancellationToken cancellationToken = default);

    /// <summary>POST /api/v1/models/download -- download a model from the catalog or Hugging Face. <paramref name="requestBodyJson"/> is forwarded verbatim.</summary>
    Task<LmStudioResponse> DownloadModelAsync(string requestBodyJson, CancellationToken cancellationToken = default);

    /// <summary>GET /api/v1/models/download/status/{jobId} -- poll a download job's progress.</summary>
    Task<LmStudioResponse> GetDownloadStatusAsync(string jobId, CancellationToken cancellationToken = default);
}