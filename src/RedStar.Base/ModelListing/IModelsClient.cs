namespace RedStar.Base.ModelListing;

public interface IModelsClient
{
    Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default);
}