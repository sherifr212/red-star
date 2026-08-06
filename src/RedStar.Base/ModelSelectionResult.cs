namespace RedStar.Base;

/// <summary>How a <see cref="ModelSelectionResult"/>'s model was picked.</summary>
public enum ModelSelectionSource
{
    /// <summary>Came from <see cref="RedStarOptions.DefaultModel"/>, verified to be loaded.</summary>
    Explicit,

    /// <summary>No default was configured; the server's single loaded model was used instead.</summary>
    Implicit,
}

/// <summary>
/// Outcome of <see cref="ModelSelector.SelectDefault"/>. On success, <see cref="Model"/>/<see cref="Source"/>
/// are set and <see cref="InfoMessage"/> is non-null when the choice is worth surfacing to the user (the
/// implicit single-loaded-model case). On failure, <see cref="ErrorMessage"/> is a human-readable reason
/// callers should print and then exit -- every failure mode here means continuing would talk to a model the
/// user didn't ask for, so there is no safe fallback to guess one instead.
/// </summary>
public sealed record ModelSelectionResult
{
    public ModelInfo? Model { get; private init; }

    public ModelSelectionSource? Source { get; private init; }

    public string? InfoMessage { get; private init; }

    public string? ErrorMessage { get; private init; }

    public bool Succeeded => Model is not null;

    public static ModelSelectionResult Ok(ModelInfo model, ModelSelectionSource source, string? infoMessage = null) =>
        new() { Model = model, Source = source, InfoMessage = infoMessage };

    public static ModelSelectionResult Fail(string errorMessage) => new() { ErrorMessage = errorMessage };
}
