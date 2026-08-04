namespace RedStar.Base;

/// <summary>
/// Picks which model a request should use when none was given explicitly for that call.
/// </summary>
public static class ModelSelector
{
    /// <summary>
    /// Resolution order: an explicitly configured default model, if the server's model list
    /// contains it (returned even when <see cref="ModelInfo.Loaded"/> is false, since Unsloth
    /// Studio can load a known-but-unloaded model on demand); if a default is configured but
    /// absent from the list entirely, resolution fails (<c>null</c>) rather than trusting an
    /// unverifiable id -- otherwise whichever model the server reports as loaded; otherwise the
    /// first model the server knows about; otherwise <c>null</c> if the server has no models at
    /// all.
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

            return null;
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
