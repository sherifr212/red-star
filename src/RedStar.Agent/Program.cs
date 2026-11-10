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

                services.AddTransient<IAgentFactory, AgentFactory>();
                services.AddHostedService<AgentApp>();
            })
            .Build();

        await host.RunAsync();
    }
}

public sealed partial class AgentApp : BackgroundService
{
    private readonly RedStarOptions _options;
    private readonly IAgentFactory _agentFactory;
    private readonly ILogger<AgentApp> _logger;
    private readonly IHostApplicationLifetime _lifetime;

    public AgentApp(RedStarOptions options, IAgentFactory agentFactory, ILogger<AgentApp> logger, IHostApplicationLifetime lifetime)
    {
        _options = options;
        _agentFactory = agentFactory;
        _logger = logger;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(System.Threading.CancellationToken stoppingToken)
    {
        var agentName = _options.Agent ?? AgentNames.Unsloth;

        LogStartingAgentApp(agentName);

        var agent = _agentFactory.CreateAgent(agentName, out string defaultModel);

        var session = new ChatSession(agent);

        await Console.Out.WriteLineAsync($"Starting RedStar.Agent chat loop (Model: {defaultModel}, Agent: {agentName}).");
        await Console.Out.WriteLineAsync("Type 'exit' or 'quit' to quit.");
        await Console.Out.WriteLineAsync("---------------------------------------");

        using var cts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _lifetime.ApplicationStopping);
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        while (!cts.IsCancellationRequested)
        {
            await Console.Out.WriteAsync("> ");

            string? input = null;
            try
            {
                input = await Task.Run(() => Console.In.ReadLineAsync(), cts.Token);
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
                await Console.Out.WriteLineAsync("\nAgent: ");
                await session.SendAsync(input, cancellationToken: cts.Token, onTextChunk: chunk =>
                {
                    Console.Write(chunk);
                });
                await Console.Out.WriteLineAsync("\n");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch(Exception ex)
            {
                LogError(ex, "Error processing chat");
                await Console.Out.WriteLineAsync($"\nError: {ex.Message}");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting AgentApp with agent {AgentName}")]
    private partial void LogStartingAgentApp(string agentName);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Message}")]
    private partial void LogError(Exception ex, string message);
}
