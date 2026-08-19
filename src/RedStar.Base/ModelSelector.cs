namespace RedStar.Base;

/// <summary>
/// Picks which model a startup should use. Every outcome requires at least one model to be
/// currently loaded on the server -- an id the server merely knows about but hasn't loaded isn't
/// usable, unless <paramref name="allowJitLoad"/> lets the configured default through anyway (see
/// <see cref="SelectDefault"/>). Also excludes embeddings-type models (<see cref="ModelInfo.Type"/>
/// <c>"embeddings"</c>) from every implicit/ambiguity consideration -- an embeddings model can't serve a
/// chat request, so it should never get auto-selected or count toward "multiple models loaded".
/// </summary>
public static class ModelSelector
{
    /// <summary>
    /// Resolution rules, checked in order:
    /// <list type="bullet">
    /// <item>When <paramref name="allowJitLoad"/> is true and <paramref name="configuredDefault"/> names a
    /// chat-capable model the server knows about but hasn't currently loaded, succeed immediately with
    /// <see cref="ModelSelectionSource.PendingJitLoad"/> -- trusting the server to load it on the first
    /// request. Skipped entirely when <paramref name="allowJitLoad"/> is false (the default), so this never
    /// changes behavior for a caller that doesn't opt in.</item>
    /// <item>No (chat-capable) model is loaded at all -- fails.</item>
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
    /// <param name="allowJitLoad">
    /// Whether an unloaded-but-known configured default should succeed anyway instead of failing, on the
    /// assumption the server will load it just-in-time on first use (LM Studio's behavior; Unsloth has no
    /// such capability, so callers building an Unsloth agent should leave this false).
    /// </param>
    public static ModelSelectionResult SelectDefault(
        IReadOnlyList<ModelInfo> models, string? configuredDefault, bool allowJitLoad = false)
    {
        ArgumentNullException.ThrowIfNull(models);

        var chatModels = models.Where(IsChatCapable).ToList();
        var loaded = chatModels.Where(m => m.Loaded).ToList();

        if (allowJitLoad && !string.IsNullOrWhiteSpace(configuredDefault))
        {
            var knownMatch = chatModels.FirstOrDefault(m => string.Equals(m.Id, configuredDefault, StringComparison.Ordinal));
            if (knownMatch is not null && !knownMatch.Loaded)
            {
                return ModelSelectionResult.Ok(
                    knownMatch, ModelSelectionSource.PendingJitLoad,
                    $"'{configuredDefault}' is not currently loaded; the server will load it on the first request (this may take a moment).");
            }
        }

        if (loaded.Count == 0)
        {
            return ModelSelectionResult.Fail(
                "No models are currently loaded on the server. Load one, then try again.");
        }

        if (!string.IsNullOrWhiteSpace(configuredDefault))
        {
            var match = loaded.FirstOrDefault(m => string.Equals(m.Id, configuredDefault, StringComparison.Ordinal));
            if (match is not null)
            {
                return ModelSelectionResult.Ok(match, ModelSelectionSource.Explicit);
            }

            var loadedIds = string.Join(", ", loaded.Select(m => m.Id));
            var knownButUnloaded = chatModels.Any(m => string.Equals(m.Id, configuredDefault, StringComparison.Ordinal));
            var reason = knownButUnloaded
                ? "it is known to the server but not currently loaded"
                : "it was not found on the server at all";
            return ModelSelectionResult.Fail(
                $"Configured default model '{configuredDefault}' can't be used: {reason}. " +
                $"Currently loaded model(s): {loadedIds}. Load '{configuredDefault}', or point " +
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

    /// <summary>True unless <paramref name="model"/> is explicitly reported as an embeddings model -- a null
    /// <see cref="ModelInfo.Type"/> (e.g. every Unsloth model, which reports no type at all) is treated as
    /// chat-capable rather than excluded, since "unknown" isn't evidence it's an embeddings model.</summary>
    private static bool IsChatCapable(ModelInfo model) =>
        model.Type is null || !string.Equals(model.Type, "embeddings", StringComparison.OrdinalIgnoreCase);
}