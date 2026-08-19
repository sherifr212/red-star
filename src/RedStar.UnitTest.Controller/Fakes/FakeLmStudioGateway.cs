using RedStar.Controller;

namespace RedStar.UnitTest.Controller.Fakes;

internal sealed class FakeLmStudioGateway : ILmStudioGateway
{
    public LmStudioResponse Response { get; set; } = new(200, "{}");

    public string? LastMethod { get; private set; }
    public string? LastArgument { get; private set; }

    public Task<LmStudioResponse> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        LastMethod = nameof(GetModelsAsync);
        return Task.FromResult(Response);
    }

    public Task<LmStudioResponse> LoadModelAsync(string requestBodyJson, CancellationToken cancellationToken = default)
    {
        LastMethod = nameof(LoadModelAsync);
        LastArgument = requestBodyJson;
        return Task.FromResult(Response);
    }

    public Task<LmStudioResponse> UnloadModelAsync(string requestBodyJson, CancellationToken cancellationToken = default)
    {
        LastMethod = nameof(UnloadModelAsync);
        LastArgument = requestBodyJson;
        return Task.FromResult(Response);
    }

    public Task<LmStudioResponse> DownloadModelAsync(string requestBodyJson, CancellationToken cancellationToken = default)
    {
        LastMethod = nameof(DownloadModelAsync);
        LastArgument = requestBodyJson;
        return Task.FromResult(Response);
    }

    public Task<LmStudioResponse> GetDownloadStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        LastMethod = nameof(GetDownloadStatusAsync);
        LastArgument = jobId;
        return Task.FromResult(Response);
    }
}