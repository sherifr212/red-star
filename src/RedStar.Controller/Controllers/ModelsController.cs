using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace RedStar.Controller.Controllers;

/// <summary>
/// Implements LM Studio's native v1 REST API for model management (list/load/unload/download/download
/// status) as an exact passthrough gateway -- same paths, same request/response JSON shapes as
/// documented at https://lmstudio.ai/docs/developer/rest, just proxied through RedStar so callers
/// never need the LM Studio server's URL or bearer token directly. <c>POST /api/v1/chat</c> (inference)
/// is deliberately not implemented here -- that's a specialized agent's responsibility to initiate and
/// consume the streamed response, per RedStar's agent-per-provider architecture (see CLAUDE.md).
/// </summary>
[ApiController]
[Route("api/v1/models")]
public sealed class ModelsController(ILmStudioGateway gateway) : ControllerBase
{
    /// <summary>GET /api/v1/models -- list all loaded and downloaded models.</summary>
    [HttpGet]
    public async Task<IActionResult> GetModels(CancellationToken cancellationToken)
    {
        var response = await gateway.GetModelsAsync(cancellationToken);
        return FromLmStudioResponse(response);
    }

    /// <summary>POST /api/v1/models/load -- load a model, JIT-loading it if it isn't already loaded.</summary>
    [HttpPost("load")]
    public async Task<IActionResult> LoadModel([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var response = await gateway.LoadModelAsync(body.GetRawText(), cancellationToken);
        return FromLmStudioResponse(response);
    }

    /// <summary>POST /api/v1/models/unload -- unload a loaded model instance.</summary>
    [HttpPost("unload")]
    public async Task<IActionResult> UnloadModel([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var response = await gateway.UnloadModelAsync(body.GetRawText(), cancellationToken);
        return FromLmStudioResponse(response);
    }

    /// <summary>POST /api/v1/models/download -- download a model from the catalog or Hugging Face.</summary>
    [HttpPost("download")]
    public async Task<IActionResult> DownloadModel([FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var response = await gateway.DownloadModelAsync(body.GetRawText(), cancellationToken);
        return FromLmStudioResponse(response);
    }

    /// <summary>GET /api/v1/models/download/status/{jobId} -- poll a download job's progress.</summary>
    [HttpGet("download/status/{jobId}")]
    public async Task<IActionResult> GetDownloadStatus(string jobId, CancellationToken cancellationToken)
    {
        var response = await gateway.GetDownloadStatusAsync(jobId, cancellationToken);
        return FromLmStudioResponse(response);
    }

    private static ContentResult FromLmStudioResponse(LmStudioResponse response) =>
        new() { StatusCode = response.StatusCode, Content = response.Body, ContentType = "application/json" };
}
