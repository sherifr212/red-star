using System.CommandLine;
using Microsoft.Extensions.Configuration;
using RedStar.Base;
using RedStar.Cli;

var baseUrlOption = new Option<string?>("--endpoint")
{
    Description = "Base URL of the OpenAI-compatible API (default: http://127.0.0.1:8888/v1).",
};
var apiKeyOption = new Option<string?>("--api-key")
{
    Description = "Bearer API key for the server. Falls back to RedStar__ApiKey env var or appsettings.local.json.",
};
var modelOption = new Option<string?>("--model", "-m")
{
    Description = "Model id to use for this call. Overrides the configured default model " +
                  "(RedStar__DefaultModel) and auto-detection.",
};
var promptOption = new Option<string?>("--prompt", "-p")
{
    Description = "Send a single prompt and print the response, then exit. Omit for an interactive session.",
};
var systemOption = new Option<string?>("--system", "-s")
{
    Description = "Optional system prompt to prime the conversation.",
};

// Shared across the root command and 'chat', so `redstar -p "hi"` and `redstar chat -p "hi"`
// behave identically regardless of which other options are also present.
var chatOptions = new[] { baseUrlOption, apiKeyOption, modelOption, promptOption, systemOption };

var chatCommand = new Command("chat", "Chat with the locally hosted LLM (Unsloth Studio).");
foreach (var option in chatOptions)
{
    chatCommand.Options.Add(option);
}

chatCommand.SetAction((parseResult, cancellationToken) => RunChatAsync(parseResult, cancellationToken));

var modelsCommand = new Command("models", "List models available on the server.");
modelsCommand.Options.Add(baseUrlOption);
modelsCommand.Options.Add(apiKeyOption);
modelsCommand.SetAction(async (parseResult, cancellationToken) =>
{
    var options = BuildOptions(parseResult, baseUrlOption, apiKeyOption, modelOption: null);
    return await ModelsCommandHandler.RunAsync(options, cancellationToken);
});

var root = new RootCommand("RedStar - CLI for talking to a locally hosted LLM (Unsloth Studio) over its OpenAI-compatible API.");
foreach (var option in chatOptions)
{
    root.Options.Add(option);
}

root.Subcommands.Add(chatCommand);
root.Subcommands.Add(modelsCommand);
root.SetAction((parseResult, cancellationToken) => RunChatAsync(parseResult, cancellationToken));

return await root.Parse(args).InvokeAsync();

Task<int> RunChatAsync(ParseResult parseResult, CancellationToken cancellationToken)
{
    var options = BuildOptions(parseResult, baseUrlOption, apiKeyOption, modelOption);
    var prompt = parseResult.GetValue(promptOption);
    var systemPrompt = parseResult.GetValue(systemOption);
    return ChatCommandHandler.RunAsync(options, prompt, systemPrompt, cancellationToken);
}

static RedStarOptions BuildOptions(
    ParseResult parseResult,
    Option<string?> baseUrlOpt,
    Option<string?> apiKeyOpt,
    Option<string?>? modelOption)
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();

    var options = new RedStarOptions();
    configuration.GetSection(RedStarOptions.SectionName).Bind(options);

    return options.ApplyOverrides(
        baseUrl: parseResult.GetValue(baseUrlOpt),
        apiKey: parseResult.GetValue(apiKeyOpt),
        defaultModel: modelOption is null ? null : parseResult.GetValue(modelOption));
}
