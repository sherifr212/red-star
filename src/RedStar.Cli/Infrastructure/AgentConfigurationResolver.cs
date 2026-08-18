using RedStar.Base;
using RedStar.Base.Agents.ClaudeCode;
using RedStar.Base.Agents.GoogleAI;
using RedStar.Base.Agents.Unsloth;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RedStar.Cli.Infrastructure;

/// <summary>
/// One agent's resolved connection settings for this run, picked from <see cref="RedStarOptions.Agent"/>.
/// <see cref="Tools"/> is null for an agent with no such concept, rather than an empty list.
/// <see cref="KnownToolNames"/> is the catalog checked against <see cref="Tools"/>.
/// <see cref="BaseUrl"/>/<see cref="ApiKey"/> are both empty for ClaudeCode.
/// </summary>
internal readonly record struct ActiveAgentSettings(
    string AgentName, string BaseUrl, string ApiKey, IReadOnlyList<string>? Tools, IReadOnlyList<string>? KnownToolNames);

internal static class AgentConfigurationResolver
{
    public static ActiveAgentSettings Resolve(RedStarOptions options)
    {
        if (string.Equals(options.Agent, AgentNames.LMStudio, StringComparison.OrdinalIgnoreCase))
        {
            return new ActiveAgentSettings(AgentNames.LMStudio, options.Agents.LMStudio.BaseUrl, options.Agents.LMStudio.ApiKey, null, null);
        }

        if (string.Equals(options.Agent, AgentNames.GoogleAI, StringComparison.OrdinalIgnoreCase))
        {
            var enabledHostedTools = options.Agents.GoogleAI.HostedTools
                .Where(kvp => kvp.Value)
                .Select(kvp => kvp.Key)
                .ToList();
            return new ActiveAgentSettings(
                AgentNames.GoogleAI, options.Agents.GoogleAI.BaseUrl, options.Agents.GoogleAI.ApiKey,
                enabledHostedTools, GoogleAIHostedTools.Known);
        }

        if (string.Equals(options.Agent, AgentNames.ClaudeCode, StringComparison.OrdinalIgnoreCase))
        {
            return new ActiveAgentSettings(
                AgentNames.ClaudeCode, "", "", options.Agents.ClaudeCode.AllowedTools, ClaudeCodeTools.Known);
        }

        return new ActiveAgentSettings(
            AgentNames.Unsloth, options.Agents.Unsloth.BaseUrl, options.Agents.Unsloth.ApiKey,
            options.Agents.Unsloth.EnabledTools, UnslothTools.Known);
    }
}
