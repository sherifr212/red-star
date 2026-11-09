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

                services.AddTransient<AITool>(sp => AIFunctionFactory.Create(sp.GetRequiredService<ReadTool>().ReadAsync, "read"));
                services.AddTransient<AITool>(sp => AIFunctionFactory.Create(sp.GetRequiredService<WriteTool>().WriteAsync, "write"));
                services.AddTransient<AITool>(sp => AIFunctionFactory.Create(sp.GetRequiredService<EditTool>().EditAsync, "edit"));
                services.AddTransient<AITool>(sp => AIFunctionFactory.Create(sp.GetRequiredService<BashTool>().RunBashAsync, "bash"));

                services.AddHostedService<AgentApp>();
            })
            .Build();

        await host.RunAsync();
    }
}

public sealed partial class AgentApp : BackgroundService
{
    private readonly RedStarOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<AITool> _tools;
    private readonly ILogger<AgentApp> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public AgentApp(RedStarOptions options, IServiceProvider serviceProvider, IEnumerable<AITool> tools, ILogger<AgentApp> logger, IHostApplicationLifetime lifetime)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _tools = tools;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
    {
        var agentName = _options.Agent ?? AgentNames.Unsloth;

        LogStartingAgentApp(agentName);

        string systemPrompt = string.IsNullOrWhiteSpace(_options.SystemPrompt)
            ? "You are an agentic console application. Use tools appropriately to fulfill the user request."
            : _options.SystemPrompt;
        string defaultModel = "";

        var tools = _tools.ToList();

        AIAgent agent;
        if (string.Equals(agentName, AgentNames.LMStudio, StringComparison.OrdinalIgnoreCase))
        {
             defaultModel = _options.Agents.LMStudio.DefaultModel;
             var client = _serviceProvider.GetRequiredService<LMStudioHttpClient>().HttpClient;
             agent = LMStudioAgentFactory.Create(client, _options, defaultModel, systemPrompt, tools);
        }
        else if (string.Equals(agentName, AgentNames.GoogleAI, StringComparison.OrdinalIgnoreCase))
        {
             defaultModel = _options.Agents.GoogleAI.DefaultModel;
             agent = GoogleAIAgentFactory.Create(() => _serviceProvider.GetRequiredService<GoogleAIHttpClient>().HttpClient, _options, defaultModel, systemPrompt, tools, null);
        }
        else if (string.Equals(agentName, AgentNames.ClaudeCode, StringComparison.OrdinalIgnoreCase))
        {
             defaultModel = _options.Agents.ClaudeCode.DefaultModel;
             agent = ClaudeCodeAgentFactory.Create(_options, defaultModel, systemPrompt, tools);
        }
        else
        {
             defaultModel = _options.Agents.Unsloth.DefaultModel;
             var client = _serviceProvider.GetRequiredService<UnslothHttpClient>().HttpClient;
             agent = UnslothAgentFactory.Create(client, _options, defaultModel, systemPrompt, tools);
        }

        var session = new ChatSession(agent);

        Console.WriteLine($"Starting RedStar.Agent chat loop (Model: {defaultModel}, Agent: {agentName}).");
        Console.WriteLine("Type 'exit' or 'quit' to quit.");
        Console.WriteLine("---------------------------------------");

        using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifetime.ApplicationStopping);
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        while (!cts.IsCancellationRequested)
        {
            Console.Write("> ");

            string? input = null;
            try
            {
                input = await Task.Run(() => Console.ReadLine(), cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                _lifetime.StopApplication();
                break;
            }

            try
            {
                Console.WriteLine("\nAgent: ");
                await session.SendAsync(input, cancellationToken: cts.Token, onTextChunk: chunk =>
                {
                    Console.Write(chunk);
                });
                Console.WriteLine("\n");
            }
            catch (OperationCanceledException)
            {
                break;
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
