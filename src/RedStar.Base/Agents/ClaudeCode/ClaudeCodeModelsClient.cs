namespace RedStar.Base.Agents.ClaudeCode;

/// <summary>
/// Lists ClaudeCode's model aliases via a fixed catalog rather than any HTTP/process call -- unlike
/// <c>ModelsClient</c>/<c>LMStudioModelsClient</c>, there is no <c>/models</c> endpoint or equivalent to
/// query: the <c>claude</c> CLI resolves <c>--model</c> at request time with no separate "list what's
/// available" step. Every entry is reported <c>Loaded: true</c> since ClaudeCode has no
/// loaded/not-loaded distinction for <see cref="ModelSelector"/> to key off -- though in practice
/// <c>ChatCommandHandler</c> never calls <see cref="ModelSelector.SelectDefault"/> for this agent at all
/// (see <see cref="ClaudeCodeAgentOptions.DefaultModel"/>'s remarks), so this only actually matters for
/// <c>redstar models</c>'s own display, not for chat's model resolution.
/// </summary>
public sealed class ClaudeCodeModelsClient : IModelsClient
{
    /// <summary>Short aliases the CLI's <c>--model</c> flag documents (<c>claude --help</c>, v2.1.224):
    /// "Provide an alias for the latest model (e.g. 'fable', 'opus', or 'sonnet') or a model's full name".
    /// A full model id (e.g. <c>claude-sonnet-5</c>) also works as <see cref="ClaudeCodeAgentOptions.DefaultModel"/>
    /// even though it isn't listed here -- this is a curated "known aliases" catalog for display, not an
    /// exhaustive/validated list.</summary>
    public static readonly IReadOnlyList<string> KnownModelAliases = ["sonnet", "opus", "haiku", "fable"];

    public Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModelInfo>>(KnownModelAliases.Select(alias => new ModelInfo(alias, Loaded: true)).ToList());
}
