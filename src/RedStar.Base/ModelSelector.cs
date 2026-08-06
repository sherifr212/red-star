namespace RedStar.Base;

/// <summary>
/// Picks which model a startup should use. Every outcome requires at least one model to be
/// currently loaded on the server -- an id the server merely knows about but hasn't loaded isn't
/// usable, since Unsloth Studio serves requests against loaded models only.
/// </summary>
public static class ModelSelector
{
    /// <summary>
    /// Resolution rules, checked in order:
    /// <list type="bullet">
    /// <item>No model is loaded at all -- fails.</item>
    /// <item>A default model is configured -- it must be one of the currently loaded models, or
    /// resolution fails even though something else happens to be loaded; silently substituting a
    /// different model than the one the user configured would be misleading. Succeeds with
    /// <see cref="ModelSelectionSource.Explicit"/>.</item>
    /// <item>No default is configured and exactly one model is loaded -- that model is used,
    /// succeeding with <see cref="ModelSelectionSource.Implicit"/> and an <see cref="ModelSelectionResult.InfoMessage"/>
    /// since the caller didn't ask for it by name.</item>
    /// <item>No default is configured and more than one model is loaded -- ambiguous, fails.</item>
    /// </list>
    /// </summary>
    public static ModelSelectionResult SelectDefault(IReadOnlyList<ModelInfo> models, string? configuredDefault)
    {
        ArgumentNullException.ThrowIfNull(models);

        var loaded = models.Where(m => m.Loaded).ToList();

        if (loaded.Count == 0)
        {
            return ModelSelectionResult.Fail(
                "No models are currently loaded on the server. Load one in Unsloth Studio, then try again.");
        }

        if (!string.IsNullOrWhiteSpace(configuredDefault))
        {
            var match = loaded.FirstOrDefault(m => string.Equals(m.Id, configuredDefault, StringComparison.Ordinal));
            if (match is not null)
            {
                return ModelSelectionResult.Ok(match, ModelSelectionSource.Explicit);
            }

            var loadedIds = string.Join(", ", loaded.Select(m => m.Id));
            var knownButUnloaded = models.Any(m => string.Equals(m.Id, configuredDefault, StringComparison.Ordinal));
            var reason = knownButUnloaded
                ? "it is known to the server but not currently loaded"
                : "it was not found on the server at all";
            return ModelSelectionResult.Fail(
                $"Configured default model '{configuredDefault}' can't be used: {reason}. " +
                $"Currently loaded model(s): {loadedIds}. Load '{configuredDefault}' in Unsloth Studio, or point " +
                "the configured model at one of the loaded ones instead.");
        }

        if (loaded.Count == 1)
        {
            var only = loaded[0];
            return ModelSelectionResult.Ok(
                only,
                ModelSelectionSource.Implicit,
                $"No default model is configured; using the only loaded model, '{only.Id}'.");
        }

        var ids = string.Join(", ", loaded.Select(m => m.Id));
        return ModelSelectionResult.Fail(
            $"Multiple models are loaded ({ids}) and no default model is configured, so the choice is " +
            "ambiguous. Set a default model to one of them.");
    }
}
