using RedStar.Base;
using RedStar.Base.Agents.GoogleAI;
using RedStar.Cli.Infrastructure;

namespace RedStar.UnitTest.Cli;

public class AgentConfigurationResolverTests
{
    [Fact]
    public void Resolve_SurfacesEnabledHostedTools_ForGoogleAI()
    {
        var options = new RedStarOptions
        {
            Agent = AgentNames.GoogleAI,
            Agents = new AgentsOptions
            {
                GoogleAI = new GoogleAIAgentOptions
                {
                    HostedTools = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        [GoogleAIHostedTools.GoogleSearch] = true,
                        [GoogleAIHostedTools.CodeExecution] = false,
                        [GoogleAIHostedTools.UrlContext] = true,
                    },
                },
            },
        };

        var active = AgentConfigurationResolver.Resolve(options);

        Assert.Equal(AgentNames.GoogleAI, active.AgentName);
        Assert.NotNull(active.Tools);
        Assert.Contains(GoogleAIHostedTools.GoogleSearch, active.Tools!);
        Assert.Contains(GoogleAIHostedTools.UrlContext, active.Tools!);
        Assert.DoesNotContain(GoogleAIHostedTools.CodeExecution, active.Tools!);
        Assert.Equal(GoogleAIHostedTools.Known, active.KnownToolNames);
    }
}