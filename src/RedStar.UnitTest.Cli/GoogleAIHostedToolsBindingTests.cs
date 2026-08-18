using Microsoft.Extensions.Configuration;
using RedStar.Base;
using RedStar.Base.Agents.GoogleAI;

namespace RedStar.UnitTest.Cli;

/// <summary>
/// Regression coverage for a real bug found in self-review: <see cref="GoogleAIAgentOptions.HostedTools"/>
/// is bound from JSON config by <c>Microsoft.Extensions.Configuration</c>'s dictionary binder, which
/// mutates the existing dictionary in place rather than replacing it -- so whether a differently-cased
/// key (e.g. the natural camelCase <c>"googleSearch"</c>, matching Gemini's own REST field naming) merges
/// into the existing <c>"GoogleSearch"</c> entry or silently becomes an unrelated second entry depends
/// entirely on that dictionary's comparer. Lives in <c>RedStar.UnitTest.Cli</c> rather than
/// <c>RedStar.UnitTest</c> because it needs <c>Microsoft.Extensions.Configuration.Binder</c>, which only
/// <c>RedStar.Cli</c> references -- it exercises <see cref="RedStarOptions"/> config binding, not any
/// <c>RedStar.Cli</c> type.
/// </summary>
public class GoogleAIHostedToolsBindingTests
{
    [Theory]
    [InlineData("googleSearch")]
    [InlineData("GOOGLESEARCH")]
    [InlineData("GoogleSearch")]
    public void HostedTools_BindsCaseInsensitively_RegardlessOfConfiguredKeyCasing(string configuredKey)
    {
        var json = $$"""
        {
          "RedStar": {
            "Agents": {
              "GoogleAI": {
                "HostedTools": { "{{configuredKey}}": true }
              }
            }
          }
        }
        """;

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            .Build();

        var options = new RedStarOptions();
        configuration.GetSection(RedStarOptions.SectionName).Bind(options);

        var hostedTools = options.Agents.GoogleAI.HostedTools;

        Assert.Equal(3, hostedTools.Count);
        Assert.True(hostedTools[GoogleAIHostedTools.GoogleSearch]);
        Assert.False(hostedTools[GoogleAIHostedTools.CodeExecution]);
        Assert.False(hostedTools[GoogleAIHostedTools.UrlContext]);
    }
}
