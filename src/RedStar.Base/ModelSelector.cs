namespace RedStar.Base;

/// <summary>
/// Picks which model a request should use when none was given explicitly for that call.
/// </summary>
public static class ModelSelector
{
    /// <summary>
    /// Resolution order: an explicitly configured default model (trusted even if the server
    /// doesn't currently report it as available, since it may be loadable on demand); otherwise
    /// whichever model the server reports as loaded; otherwise the first model the server knows
    /// about; otherwise <c>null</c> if the server has no models at all.
    /// </summary>
    public static ModelInfo? SelectDefault(IReadOnlyList<ModelInfo> models, string? configuredDefault)
    {
        ArgumentNullException.ThrowIfNull(models);

        if (!string.IsNullOrWhiteSpace(configuredDefault))
        {
            foreach (var model in models)
            {
                if (string.Equals(model.Id, configuredDefault, StringComparison.Ordinal))
                {
                    return model;
                }
            }

            return new ModelInfo(configuredDefault, Loaded: false);
        }

        foreach (var model in models)
        {
            if (model.Loaded)
            {
                return model;
            }
        }

        return models.Count > 0 ? models[0] : null;
    }
}
