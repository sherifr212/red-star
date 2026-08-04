using Microsoft.Extensions.AI;
using RedStar.Base;

namespace RedStar.Cli;

internal static class ChatCommandHandler
{
    public static async Task<int> RunAsync(
        RedStarOptions options,
        string? oneShotPrompt,
        string? systemPrompt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.ApiKey))
        {
            Console.Error.WriteLine(
                "Warning: no API key configured. Unsloth Studio requires a bearer token for /v1 calls.\n" +
                "Generate one from the Unsloth Studio UI (Settings -> API Keys), then set it via\n" +
                "--api-key, the RedStar__ApiKey environment variable, or appsettings.local.json.\n");
        }

        var modelId = options.DefaultModel;
        if (string.IsNullOrEmpty(modelId))
        {
            modelId = await ResolveDefaultModelAsync(options, cancellationToken);
            if (modelId is null)
            {
                return 1;
            }
        }

        IChatClient chatClient = RedStarChatClientFactory.Create(options, modelId);
        var chatOptions = RedStarChatClientFactory.CreateChatOptions(options);
        var session = new ChatSession();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            session.AddSystemPrompt(systemPrompt);
        }

        if (!string.IsNullOrWhiteSpace(oneShotPrompt))
        {
            session.AddUserMessage(oneShotPrompt);
            return await SendAndPrintAsync(session, chatClient, chatOptions, cancellationToken);
        }

        Console.WriteLine($"RedStar chat - model '{modelId}'. Type 'exit' or press Ctrl+C to quit.");
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("\nyou> ");
            var line = Console.ReadLine();
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

            session.AddUserMessage(line);
            Console.Write("assistant> ");
            var exitCode = await SendAndPrintAsync(session, chatClient, chatOptions, cancellationToken);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        return 0;
    }

    private static async Task<int> SendAndPrintAsync(
        ChatSession session, IChatClient chatClient, ChatOptions? chatOptions, CancellationToken cancellationToken)
    {
        try
        {
            await session.SendAsync(chatClient, chatOptions, onTextChunk: Console.Write, cancellationToken: cancellationToken);
            Console.WriteLine();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nError calling the model: {ex.Message}");
            return 1;
        }
    }

    private static async Task<string?> ResolveDefaultModelAsync(RedStarOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var modelsClient = new ModelsClient(options);
            var models = await modelsClient.ListAsync(cancellationToken);
            var selected = ModelSelector.SelectDefault(models, configuredDefault: null);
            if (selected is null)
            {
                Console.Error.WriteLine("No models are available on the server. Load one in Unsloth Studio first.");
                return null;
            }

            return selected.Id;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"Could not auto-detect a model ({ex.Message}). Pass --model explicitly or run 'redstar models'.");
            return null;
        }
    }
}
