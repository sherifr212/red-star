using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using RedStar.Base;
using RedStar.Base.Agents.ClaudeCode;
using RedStar.Base.Agents.GoogleAI;
using RedStar.Base.Agents.LMStudio;
using RedStar.Base.Agents.Unsloth;

namespace RedStar.Agent;

public interface IAgentFactory
{
    AIAgent CreateAgent(string agentName, out string defaultModel);
}

public class AgentFactory : IAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RedStarOptions _options;
    private readonly IEnumerable<AITool> _tools;

    public AgentFactory(IServiceProvider serviceProvider, RedStarOptions options, IEnumerable<AITool> tools)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _tools = tools;
    }

    public AIAgent CreateAgent(string agentName, out string defaultModel)
    {
        string systemPrompt = string.IsNullOrWhiteSpace(_options.SystemPrompt)
            ? "You are an agentic console application. Use tools appropriately to fulfill the user request."
            : _options.SystemPrompt;

        var tools = _tools.ToList();

        if (string.Equals(agentName, AgentNames.LMStudio, StringComparison.OrdinalIgnoreCase))
        {
            defaultModel = _options.Agents.LMStudio.DefaultModel;
            var client = _serviceProvider.GetRequiredService<LMStudioHttpClient>().HttpClient;
            return LMStudioAgentFactory.Create(client, _options, defaultModel, systemPrompt, tools);
        }
        else if (string.Equals(agentName, AgentNames.GoogleAI, StringComparison.OrdinalIgnoreCase))
        {
            defaultModel = _options.Agents.GoogleAI.DefaultModel;
            return GoogleAIAgentFactory.Create(() => _serviceProvider.GetRequiredService<GoogleAIHttpClient>().HttpClient, _options, defaultModel, systemPrompt, tools, null);
        }
        else if (string.Equals(agentName, AgentNames.ClaudeCode, StringComparison.OrdinalIgnoreCase))
        {
            defaultModel = _options.Agents.ClaudeCode.DefaultModel;
            return ClaudeCodeAgentFactory.Create(_options, defaultModel, systemPrompt, tools);
        }
        else if (string.Equals(agentName, AgentNames.Unsloth, StringComparison.OrdinalIgnoreCase))
        {
            defaultModel = _options.Agents.Unsloth.DefaultModel;
            var client = _serviceProvider.GetRequiredService<UnslothHttpClient>().HttpClient;
            return UnslothAgentFactory.Create(client, _options, defaultModel, systemPrompt, tools);
        }

        throw new InvalidOperationException($"The agent name '{agentName}' is not recognized or supported.");
    }
}
