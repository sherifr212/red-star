namespace RedStar.Base;

/// <summary>Whether a <see cref="ModelSelectionResult"/>'s model came from the configured default or was
/// auto-detected without regard to it. See <see cref="ModelSelector.SelectDefault"/>.</summary>
public enum ModelSelectionSource
{
    /// <summary>Chosen without regard to <c>configuredDefault</c> -- the server has exactly one loaded
    /// model, so that model is used regardless of what (if anything) is configured.</summary>
    Implicit,

    /// <summary>Chosen because it matched <c>configuredDefault</c> -- required when the server has more
    /// than one loaded model, since otherwise there'd be no way to disambiguate between them.</summary>
    Explicit,
}

/// <summary>
/// Outcome of <see cref="ModelSelector.SelectDefault"/>: either a selected <see cref="Model"/> (with
/// <see cref="Source"/> explaining why it was picked, and an optional informational <see cref="Message"/>
/// worth surfacing to the user/telemetry), or a failure with an <see cref="ErrorMessage"/> explaining why
/// no model could be resolved.
/// </summary>
public sealed record ModelSelectionResult(
    ModelInfo? Model,
    ModelSelectionSource Source,
    string? Message,
    string? ErrorMessage)
{
    public bool IsSuccess => Model is not null;

    public static ModelSelectionResult Success(ModelInfo model, ModelSelectionSource source, string? message = null) =>
        new(model, source, message, ErrorMessage: null);

    public static ModelSelectionResult Failure(string errorMessage) =>
        new(Model: null, Source: ModelSelectionSource.Implicit, Message: null, errorMessage);
}

/// <summary>
/// Picks which model a request should use, out of whichever models the server currently reports as loaded.
/// </summary>
public static class ModelSelector
{
    /// <summary>
    /// Resolution only ever considers <see cref="ModelInfo.Loaded"/> models -- a model merely known to the
    /// server but not currently loaded is never selected, even if it matches <paramref name="configuredDefault"/>,
    /// since starting a chat against it would silently hang until (or fail unless) something else loads it.
    /// Rules, in order:
    /// <list type="number">
    /// <item>Zero loaded models: failure -- nothing can be selected.</item>
    /// <item>Exactly one loaded model: that model wins unconditionally (<see cref="ModelSelectionSource.Implicit"/>),
    /// regardless of <paramref name="configuredDefault"/> -- overriding it if it disagrees.</item>
    /// <item>More than one loaded model: <paramref name="configuredDefault"/> must be set and must name one
    /// of the loaded models (<see cref="ModelSelectionSource.Explicit"/>); otherwise failure, since there's
    /// no way to disambiguate automatically.</item>
    /// </list>
    /// </summary>
    public static ModelSelectionResult SelectDefault(IReadOnlyList<ModelInfo> models, string? configuredDefault)
    {
        ArgumentNullException.ThrowIfNull(models);

        var loaded = models.Where(m => m.Loaded).ToList();

        if (loaded.Count == 0)
        {
            return ModelSelectionResult.Failure(
                "No models are loaded on the server. Load one in Unsloth Studio first.");
        }

        if (loaded.Count == 1)
        {
            var only = loaded[0];
            var overridesConfig = !string.IsNullOrWhiteSpace(configuredDefault) &&
                !string.Equals(configuredDefault, only.Id, StringComparison.Ordinal);
            var message = overridesConfig
                ? $"Only one model is loaded ('{only.Id}'); using it instead of the configured default ('{configuredDefault}')."
                : $"Only one model is loaded ('{only.Id}'); using it.";

            return ModelSelectionResult.Success(only, ModelSelectionSource.Implicit, message);
        }

        var loadedIds = string.Join(", ", loaded.Select(m => m.Id));

        if (string.IsNullOrWhiteSpace(configuredDefault))
        {
            return ModelSelectionResult.Failure(
                $"Multiple models are loaded ({loadedIds}) and no default model is configured. " +
                "Set one via --model, the RedStar__DefaultModel environment variable, or appsettings.local.json.");
        }

        foreach (var model in loaded)
        {
            if (string.Equals(model.Id, configuredDefault, StringComparison.Ordinal))
            {
                return ModelSelectionResult.Success(model, ModelSelectionSource.Explicit);
            }
        }

        return ModelSelectionResult.Failure(
            $"Configured model '{configuredDefault}' is not currently loaded. Loaded models: {loadedIds}.");
    }
}
