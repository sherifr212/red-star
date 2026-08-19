using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

using BoxOfYellow.ConsoleMarkdownRenderer.Spectre;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using RedStar.Base;
using RedStar.Base.Agents.ClaudeCode;
using RedStar.Base.Agents.GoogleAI;
using RedStar.Base.Agents.LMStudio;
using RedStar.Base.Agents.Unsloth;
using RedStar.Base.Telemetry;
using RedStar.Cli.Infrastructure;
using RedStar.Cli.Rendering;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace RedStar.Cli;

internal static class ChatCommandHandler
{
    /// <param name="agentFactory">
    /// Builds the <see cref="AIAgent"/> to chat with, given (options, modelId, instructions). Defaults to
    /// <see cref="UnslothAgentFactory.Create"/> or <c>LMStudioAgentFactory.Create</c> depending on
    /// <see cref="RedStarOptions.Agent"/> (an explicit two-way switch, not a registry -- see
    /// <see cref="AgentNames"/>); tests can substitute a fake here without touching the network.
    /// </param>
    /// <param name="modelsClientFactory">
    /// Builds the <see cref="IModelsClient"/> used for auto-resolving a default model. Defaults to a real
    /// <see cref="ModelsClient"/> or <c>LMStudioModelsClient</c>, same per-agent switch as
    /// <paramref name="agentFactory"/>; tests can substitute a fake here without touching the network.
    /// </param>
    /// <param name="responseExtractor">
    /// Extracts tool-status labels and web-search hit lists from streamed updates (see
    /// <see cref="ProduceStageEventsAsync"/>). Defaults to <see cref="UnslothAgentResponseExtractor"/> or
    /// <c>LMStudioAgentResponseExtractor</c>, same per-agent switch as <paramref name="agentFactory"/>;
    /// tests can substitute a fake here without depending on real Unsloth SSE JSON shapes.
    /// </param>
    /// <param name="httpClientFactory">
    /// Factory for creating pre-configured HttpClient instances per agent (Unsloth/LMStudio's named clients
    /// carry a <see cref="ConditionalAuthHandler"/> in their pipeline, registered in Program.cs -- see its
    /// remarks for why the handler itself needs no per-call configuration). Only null in tests, which always
    /// supply agentFactory/modelsClientFactory directly.
    /// </param>
    /// <param name="runId">
    /// Correlation ID tagged onto this run's root OTel span (<c>run.correlation.id</c>). Falls back to the
    /// <c>REDSTAR_RUN_ID</c> environment variable, then a generated GUID -- every child span created for the
    /// rest of this call (chat turns, outbound HTTP calls) shares this run's trace ID automatically since
    /// they're started while this method's <see cref="Activity"/> is <see cref="Activity.Current"/>.
    /// </param>
    public static async Task<int> RunAsync(
        RedStarOptions options,
        string? oneShotPrompt,
        string? systemPrompt,
        CancellationToken cancellationToken,
        IHttpClientFactory? httpClientFactory = null,
        Func<RedStarOptions, string, string?, AIAgent>? agentFactory = null,
        Func<RedStarOptions, IModelsClient>? modelsClientFactory = null,
        IAgentResponseExtractor? responseExtractor = null,
        string? runId = null)
    {
        using var activity = RedStarTelemetry.ActivitySource.StartActivity("redstar.chat");
        runId ??= Environment.GetEnvironmentVariable("REDSTAR_RUN_ID") ?? Guid.NewGuid().ToString("N");
        activity?.SetTag("run.correlation.id", runId);

        var logger = RedStarTelemetry.CreateLogger("RedStar.Cli.ChatCommandHandler");
        logger.LogInformation("Starting redstar chat run {RunId}", runId);

        var active = AgentConfigurationResolver.Resolve(options);
        var isLMStudio = active.AgentName == AgentNames.LMStudio;
        var isClaudeCode = active.AgentName == AgentNames.ClaudeCode;
        var isGoogleAI = active.AgentName == AgentNames.GoogleAI;

        // httpClientFactory is only null in tests, which always supply agentFactory/modelsClientFactory
        // directly and so never evaluate these lambdas; production always resolves ChatCommand through DI
        // (see Program.cs), so it's non-null whenever these bodies actually run. ClaudeCode is a subprocess
        // agent, not an HTTP one (see ActiveAgentSettings' remarks), so it never touches httpClientFactory.
        agentFactory ??= isClaudeCode
            ? static (opts, modelId, instructions) => ClaudeCodeAgentFactory.Create(opts, modelId, instructions)
            : isGoogleAI
                ? (opts, modelId, instructions) => GoogleAIAgentFactory.Create(
                    () => httpClientFactory!.CreateClient(AgentNames.GoogleAI), opts, modelId, instructions)
                : isLMStudio
                    ? (opts, modelId, instructions) => LMStudioAgentFactory.Create(
                        httpClientFactory!.CreateClient(AgentNames.LMStudio), opts, modelId, instructions)
                    : (opts, modelId, instructions) => UnslothAgentFactory.Create(
                        httpClientFactory!.CreateClient(AgentNames.Unsloth), opts, modelId, instructions);
        responseExtractor ??= isClaudeCode
            ? new ClaudeCodeAgentResponseExtractor()
            : isGoogleAI
                ? new GoogleAIAgentResponseExtractor()
                : isLMStudio ? new LMStudioAgentResponseExtractor() : new UnslothAgentResponseExtractor();
        modelsClientFactory ??= isClaudeCode
            ? static opts => new ClaudeCodeModelsClient()
            : isGoogleAI
                ? opts => new GoogleAIModelsClient(httpClientFactory!.CreateClient(AgentNames.GoogleAI), opts)
                : isLMStudio
                    ? opts => new LMStudioModelsClient(httpClientFactory!.CreateClient(AgentNames.LMStudio), opts)
                    : opts => new ModelsClient(httpClientFactory!.CreateClient(AgentNames.Unsloth), opts);

        if (string.IsNullOrEmpty(active.ApiKey) && !isLMStudio && !isClaudeCode && !isGoogleAI)
        {
            ConsoleOutput.Error.MarkupLine(
                "[yellow]Warning: no API key configured.[/] Unsloth Studio requires a bearer token for /v1 calls.\n" +
                "Generate one from the Unsloth Studio UI (Settings -> API Keys), then set it via\n" +
                "--api-key, the RedStar__Agents__Unsloth__ApiKey environment variable, or appsettings.local.json.\n");
        }

        if (isClaudeCode)
        {
            WarnOnClaudeCodeAuthMisconfiguration(options.Agents.ClaudeCode);
        }

        string modelId;
        string modelSourceLabel;

        if (isClaudeCode)
        {
            // ClaudeCode has no "currently loaded models" concept for ModelSelector to resolve against --
            // the CLI resolves --model at request time with no separate listing step (see
            // ClaudeCodeAgentOptions.DefaultModel's remarks) -- so model resolution is just "pass whatever's
            // configured straight through", including empty (meaning "let the CLI use its own default").
            modelId = options.Agents.ClaudeCode.DefaultModel;
            modelSourceLabel = modelId.Length == 0 ? "CLI default" : "configured";
        }
        else
        {
            var configuredDefault = isGoogleAI
                ? options.Agents.GoogleAI.DefaultModel
                : isLMStudio ? options.Agents.LMStudio.DefaultModel : options.Agents.Unsloth.DefaultModel;
            var selection = await ResolveModelAsync(configuredDefault, isLMStudio, options, cancellationToken, modelsClientFactory);
            if (!selection.Succeeded)
            {
                logger.LogWarning("Run {RunId} aborted: model resolution failed ({Reason})", runId, selection.ErrorMessage);
                return 1;
            }

            modelId = selection.Model!.Id;
            modelSourceLabel = selection.Source!.Value switch
            {
                ModelSelectionSource.Explicit => "explicit (configured)",
                ModelSelectionSource.PendingJitLoad => "explicit (configured, loading on first request)",
                _ => "implicit (auto-detected)",
            };

            if (selection.InfoMessage is not null)
            {
                ConsoleOutput.Error.MarkupLine($"[yellow]{Markup.Escape(selection.InfoMessage)}[/]");
            }

            logger.LogInformation("Run {RunId} resolved model {ModelId} via {ModelSource}", runId, modelId, selection.Source!.Value);
        }

        ChatStartupConsole.PrintStartupInfoBox(options, active, runId, modelId, modelSourceLabel, activity, logger);

        AIAgent agent = agentFactory(options, modelId, systemPrompt);
        var session = new ChatSession(agent);

        if (!string.IsNullOrWhiteSpace(oneShotPrompt))
        {
            ChatEngineConsoleHelper.PrintUserMessageBox(oneShotPrompt);
            return await ChatEngine.SendAndPrintAsync(session, oneShotPrompt, responseExtractor, logger, cancellationToken);
        }

        var modelLabel = modelId.Length == 0 ? "(CLI default)" : modelId;
        AnsiConsole.MarkupLine(
            $"[bold]RedStar chat[/] - model '[green]{Markup.Escape(modelLabel)}[/]'. Type 'exit' or press Ctrl+C to quit.");
        while (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.WriteLine();
            var line = ChatEngineConsoleHelper.ReadUserMessageBoxed();
            if (line is null)
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var exitCode = await ChatEngine.SendAndPrintAsync(session, line, responseExtractor, logger, cancellationToken);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        return 0;
    }

    /// <summary>
    /// Warns (doesn't fail the run -- the CLI itself will surface the real auth error once it runs) about two
    /// ClaudeCode-specific misconfigurations that would otherwise only show up as a confusing subprocess
    /// failure: <see cref="ClaudeCodeAgentOptions.AuthMode"/> == <see cref="ClaudeCodeAuthModes.ApiKey"/> with
    /// a blank <see cref="ClaudeCodeAgentOptions.ApiKey"/> (mirrors the Unsloth "no API key configured"
    /// warning above), and <see cref="ClaudeCodeAgentOptions.Bare"/> combined with
    /// <see cref="ClaudeCodeAuthModes.CliLogin"/> -- <c>--bare</c> explicitly skips OAuth keychain reads,
    /// which is CliLogin's only credential source, so that combination can never authenticate.
    /// </summary>
    private static void WarnOnClaudeCodeAuthMisconfiguration(ClaudeCodeAgentOptions claudeCode)
    {
        if (string.Equals(claudeCode.AuthMode, ClaudeCodeAuthModes.ApiKey, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(claudeCode.ApiKey))
        {
            ConsoleOutput.Error.MarkupLine(
                "[yellow]Warning: no API key configured.[/] ClaudeCode's AuthMode is set to ApiKey, which requires\n" +
                "ANTHROPIC_API_KEY. Set it via --api-key, the RedStar__Agents__ClaudeCode__ApiKey environment\n" +
                "variable, or appsettings.local.json -- or switch AuthMode back to CliLogin to use a locally\n" +
                "logged-in `claude auth login` credential instead.\n");
        }

        if (claudeCode.Bare && string.Equals(claudeCode.AuthMode, ClaudeCodeAuthModes.CliLogin, StringComparison.OrdinalIgnoreCase))
        {
            ConsoleOutput.Error.MarkupLine(
                "[yellow]Warning: Bare is enabled with AuthMode CliLogin.[/] --bare explicitly skips OAuth keychain\n" +
                "reads, which is CliLogin's only credential source -- this combination has no working " +
                "credential and\nwill fail to authenticate. Set AuthMode to ApiKey (with an ApiKey configured), " +
                "or disable Bare.\n");
        }
    }

    /// <summary>
    /// Resolves and validates the model to chat with by checking it against the server's model list before
    /// any chat request is made -- this always makes the call (whether or not <paramref name="configuredDefault"/>
    /// is set) so an unloaded or nonexistent model is caught here, with a clear message, instead of surfacing
    /// later as a misleading "the model returned no response" once the chat stream unexpectedly ends empty.
    /// See <see cref="ModelSelector.SelectDefault"/> for the resolution/trust rules, including what
    /// <paramref name="allowJitLoad"/> changes.
    /// </summary>
    private static async Task<ModelSelectionResult> ResolveModelAsync(
        string? configuredDefault, bool allowJitLoad, RedStarOptions options, CancellationToken cancellationToken,
        Func<RedStarOptions, IModelsClient> modelsClientFactory)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Checking available models...", async _ =>
            {
                var modelsClient = modelsClientFactory(options);
                try
                {
                    var models = await modelsClient.ListAsync(cancellationToken);
                    var result = ModelSelector.SelectDefault(models, configuredDefault, allowJitLoad);
                    if (!result.Succeeded)
                    {
                        ConsoleOutput.Error.MarkupLine($"[red]{Markup.Escape(result.ErrorMessage!)}[/]");
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    ConsoleOutput.Error.MarkupLine(
                        $"[red]Could not check available models ({Markup.Escape(ex.Message)}).[/] " +
                        "Check --endpoint/--api-key, or run 'redstar models'.");
                    return ModelSelectionResult.Fail($"Could not check available models: {ex.Message}");
                }
            });
    }
}