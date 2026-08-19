namespace RedStar.Base;

public interface IModelsClient
{
    Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default);
}