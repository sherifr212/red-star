using RedStar.Base;

namespace RedStar.UnitTest.Cli.Fakes;

internal sealed class FakeModelsClient(IReadOnlyList<ModelInfo> models) : IModelsClient
{
    public Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(models);
}
