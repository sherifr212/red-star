using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RedStar.Base;
using RedStar.Base.Agents.ClaudeCode;
using RedStar.Base.Agents.GoogleAI;
using RedStar.Base.Agents.LMStudio;
using RedStar.Base.Agents.Unsloth;
using RedStar.Agent.Tools;

namespace RedStar.Agent;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true);
                config.AddJsonFile("appsettings.local.json", optional: true);
            })
            .ConfigureServices((context, services) =>
            {
                var options = new RedStarOptions();
                context.Configuration.GetSection("RedStar").Bind(options);
                services.AddSingleton(options);

                services.AddHttpClient<UnslothHttpClient>();
                services.AddHttpClient<LMStudioHttpClient>();
                services.AddHttpClient<GoogleAIHttpClient>();

                services.AddTransient<ReadTool>();
                services.AddTransient<WriteTool>();
                services.AddTransient<EditTool>();
                services.AddTransient<BashTool>();

                services.AddTransient<AgentApp>();
            })
            .Build();

        var app = host.Services.GetRequiredService<AgentApp>();
        await app.RunAsync();
    }
}

public sealed partial class AgentApp
{
    private readonly RedStarOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentApp> _logger;

    public AgentApp(RedStarOptions options, IServiceProvider serviceProvider, ILogger<AgentApp> logger)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var agentName = _options.Agent ?? AgentNames.Unsloth;

        LogStartingAgentApp(agentName);

        string systemPrompt = "You are an agentic console application. Use tools appropriately to fulfill the user request.";
        string defaultModel = "";

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(_serviceProvider.GetRequiredService<ReadTool>().ReadAsync, "read"),
            AIFunctionFactory.Create(_serviceProvider.GetRequiredService<WriteTool>().WriteAsync, "write"),
            AIFunctionFactory.Create(_serviceProvider.GetRequiredService<EditTool>().EditAsync, "edit"),
            AIFunctionFactory.Create(_serviceProvider.GetRequiredService<BashTool>().RunBashAsync, "bash")
        };

        AIAgent agent;
        if (string.Equals(agentName, AgentNames.LMStudio, StringComparison.OrdinalIgnoreCase))
        {
             defaultModel = _options.Agents.LMStudio.DefaultModel;
             var client = _serviceProvider.GetRequiredService<LMStudioHttpClient>().HttpClient;
             // Need to figure out how to add tools for LMStudio and Unsloth if they don't accept tools directly.
             // Looking at the implementation of LMStudioAgentFactory and UnslothAgentFactory, they don't have tools parameters
             // but instead Unsloth has enabled_tools in RedStarOptions. However, the requirement is to provide these agentic capabilities.
             // We can wrap the created AIAgent by accessing its underlying ChatClient if possible, but actually we can just pass the tools
             // when calling session.SendAsync... wait session.SendAsync doesn't accept options.
             // Wait, the agent factories return ChatClientAgent. We can reflection-set the Options or create a new ChatClientAgent.
             // But actually, we don't have ChatClientAgent options exposed.
             agent = LMStudioAgentFactory.Create(client, _options, defaultModel, systemPrompt);
        }
        else if (string.Equals(agentName, AgentNames.GoogleAI, StringComparison.OrdinalIgnoreCase))
        {
             defaultModel = _options.Agents.GoogleAI.DefaultModel;
             agent = GoogleAIAgentFactory.Create(() => _serviceProvider.GetRequiredService<GoogleAIHttpClient>().HttpClient, _options, defaultModel, systemPrompt, tools, null);
        }
        else if (string.Equals(agentName, AgentNames.ClaudeCode, StringComparison.OrdinalIgnoreCase))
        {
             defaultModel = _options.Agents.ClaudeCode.DefaultModel;
             agent = ClaudeCodeAgentFactory.Create(_options, defaultModel, systemPrompt);
        }
        else
        {
             defaultModel = _options.Agents.Unsloth.DefaultModel;
             var client = _serviceProvider.GetRequiredService<UnslothHttpClient>().HttpClient;
             agent = UnslothAgentFactory.Create(client, _options, defaultModel, systemPrompt);
        }

        // To ensure tools are available for all agents that return a ChatClientAgent, we can just replace it with a new ChatClientAgent.
        // Wait, the factories just do: return openAiClient.GetChatClient(modelId).AsAIAgent(new ChatClientAgentOptions { ChatOptions = chatOptions });
        // Since we don't have the original ChatClient directly accessible, we will inject it through a custom ChatClient wrap,
        // or just construct our own ChatClientAgent where necessary. But the easiest way is to use reflection to update the inner ChatOptions.

        var field = typeof(ChatClientAgent).GetProperty("Options", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (agent is ChatClientAgent cca && field != null)
        {
            var options = field.GetValue(cca) as ChatClientAgentOptions;
            if (options != null)
            {
                 if (options.ChatOptions == null) options.ChatOptions = new ChatOptions();
                 if (options.ChatOptions.Tools == null) options.ChatOptions.Tools = tools;
                 else
                 {
                     foreach (var t in tools) options.ChatOptions.Tools.Add(t);
                 }
            }
        }

        var session = new ChatSession(agent);

        Console.WriteLine($"Starting RedStar.Agent chat loop (Model: {defaultModel}, Agent: {agentName}).");
        Console.WriteLine("Type 'exit' or 'quit' to quit.");
        Console.WriteLine("---------------------------------------");

        while (true)
        {
            Console.Write("> ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            try
            {
                Console.WriteLine("\nAgent: ");
                await session.SendAsync(input, onTextChunk: chunk =>
                {
                    Console.Write(chunk);
                });
                Console.WriteLine("\n");
            }
            catch(Exception ex)
            {
                LogError(ex, "Error processing chat");
                Console.WriteLine($"\nError: {ex.Message}");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting AgentApp with agent {AgentName}")]
    private partial void LogStartingAgentApp(string agentName);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Message}")]
    private partial void LogError(Exception ex, string message);
}
