using RedStar.Base;
using RedStar.Base.Agents.ClaudeCode;

namespace RedStar.UnitTest;

public class RedStarOptionsTests
{
    private static RedStarOptions Original() => new()
    {
        Agents = new AgentsOptions
        {
            Unsloth = new UnslothAgentOptions
            {
                BaseUrl = "http://original/v1",
                ApiKey = "original-key",
                DefaultModel = "original-model",
            },
        },
    };

    [Fact]
    public void ApplyOverrides_AppliesAllNonBlankValues()
    {
        var result = Original().ApplyOverrides(
            baseUrl: "http://override/v1",
            apiKey: "override-key",
            defaultModel: "override-model");

        Assert.Equal("http://override/v1", result.Agents.Unsloth.BaseUrl);
        Assert.Equal("override-key", result.Agents.Unsloth.ApiKey);
        Assert.Equal("override-model", result.Agents.Unsloth.DefaultModel);
    }

    [Fact]
    public void ApplyOverrides_KeepsOriginalValues_WhenOverridesAreNull()
    {
        var original = Original();

        var result = original.ApplyOverrides(baseUrl: null, apiKey: null, defaultModel: null);

        Assert.Equal(original.Agents.Unsloth.BaseUrl, result.Agents.Unsloth.BaseUrl);
        Assert.Equal(original.Agents.Unsloth.ApiKey, result.Agents.Unsloth.ApiKey);
        Assert.Equal(original.Agents.Unsloth.DefaultModel, result.Agents.Unsloth.DefaultModel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyOverrides_KeepsOriginalValues_WhenOverridesAreBlank(string blank)
    {
        var original = Original();

        var result = original.ApplyOverrides(baseUrl: blank, apiKey: blank, defaultModel: blank);

        Assert.Equal(original.Agents.Unsloth.BaseUrl, result.Agents.Unsloth.BaseUrl);
        Assert.Equal(original.Agents.Unsloth.ApiKey, result.Agents.Unsloth.ApiKey);
        Assert.Equal(original.Agents.Unsloth.DefaultModel, result.Agents.Unsloth.DefaultModel);
    }

    [Fact]
    public void ApplyOverrides_AppliesPartialOverride_LeavingOthersUntouched()
    {
        var original = Original();

        var result = original.ApplyOverrides(defaultModel: "just-the-model");

        Assert.Equal(original.Agents.Unsloth.BaseUrl, result.Agents.Unsloth.BaseUrl);
        Assert.Equal(original.Agents.Unsloth.ApiKey, result.Agents.Unsloth.ApiKey);
        Assert.Equal("just-the-model", result.Agents.Unsloth.DefaultModel);
    }

    [Fact]
    public void EnabledTools_DefaultsToEmpty()
    {
        Assert.Empty(new RedStarOptions().Agents.Unsloth.EnabledTools);
    }

    [Fact]
    public void ApplyOverrides_PreservesEnabledTools_WhichHasNoCliOverride()
    {
        var original = Original();
        original.Agents.Unsloth.EnabledTools = ["web_search", "python"];

        var result = original.ApplyOverrides(
            baseUrl: "http://override/v1", apiKey: "override-key", defaultModel: "override-model");

        Assert.Equal(["web_search", "python"], result.Agents.Unsloth.EnabledTools);
    }

    [Fact]
    public void Otel_DefaultsToEnabledWithLocalhostEndpoint()
    {
        var otel = new RedStarOptions().Otel;

        Assert.True(otel.Enabled);
        Assert.Equal("http://localhost:4317", otel.Endpoint);
    }

    [Fact]
    public void ApplyOverrides_PreservesOtelSettings_WhichHaveNoCliOverride()
    {
        var original = Original();
        original.Otel = new OtelOptions { Enabled = false, Endpoint = "http://collector:4317" };

        var result = original.ApplyOverrides(
            baseUrl: "http://override/v1", apiKey: "override-key", defaultModel: "override-model");

        Assert.False(result.Otel.Enabled);
        Assert.Equal("http://collector:4317", result.Otel.Endpoint);
    }

    /// <summary>
    /// Guards against regressing to a field-by-field <c>ApplyOverrides</c> implementation that silently drops
    /// any property with no corresponding CLI override (the bug that dropped
    /// <see cref="UnslothAgentOptions.EnabledTools"/>'s predecessor, a single <c>WebSearchEnabled</c> bool).
    /// Walks every public settable property via
    /// reflection instead of naming them, so a future property added to <see cref="RedStarOptions"/> is
    /// covered automatically without anyone remembering to update this test.
    /// </summary>
    [Fact]
    public void ApplyOverrides_PreservesEveryProperty_WhenCalledWithNoOverrides()
    {
        var original = new RedStarOptions();
        var properties = typeof(RedStarOptions).GetProperties()
            .Where(p => p.CanRead && p.CanWrite)
            .ToArray();
        Assert.NotEmpty(properties);

        foreach (var property in properties)
        {
            object sampleValue = property.PropertyType == typeof(bool)
                ? true
                : property.PropertyType == typeof(string)
                    ? $"sample-{property.Name}"
                    : property.PropertyType == typeof(OtelOptions)
                        ? new OtelOptions { Enabled = false, Endpoint = "http://sample-otel" }
                        : property.PropertyType == typeof(AgentsOptions)
                            ? new AgentsOptions
                            {
                                Unsloth = new UnslothAgentOptions
                                {
                                    BaseUrl = "http://sample-unsloth",
                                    ApiKey = "sample-key",
                                    DefaultModel = "sample-model",
                                    EnabledTools = ["web_search", "python"],
                                },
                                LMStudio = new LMStudioAgentOptions
                                {
                                    BaseUrl = "http://sample-lmstudio",
                                    ApiKey = "sample-key",
                                    DefaultModel = "sample-model",
                                },
                            }
                            : throw new NotSupportedException(
                                $"Add a sample value for new {nameof(RedStarOptions)} property '{property.Name}' " +
                                $"of type {property.PropertyType} in this test.");
            property.SetValue(original, sampleValue);
        }

        var result = original.ApplyOverrides();

        foreach (var property in properties)
        {
            Assert.Equal(property.GetValue(original), property.GetValue(result));
        }
    }

    [Fact]
    public void Agent_DefaultsToUnsloth()
    {
        Assert.Equal(AgentNames.Unsloth, new RedStarOptions().Agent);
    }

    [Fact]
    public void ApplyOverrides_WithAgentLMStudio_OverridesLMStudioSettings_LeavingUnslothUntouched()
    {
        var original = Original();
        original.Agents = original.Agents with
        {
            LMStudio = new LMStudioAgentOptions { BaseUrl = "http://original-lmstudio/v1", ApiKey = "orig-key", DefaultModel = "orig-model" },
        };

        var result = original.ApplyOverrides(
            agent: AgentNames.LMStudio, baseUrl: "http://override/v1", apiKey: "override-key", defaultModel: "override-model");

        Assert.Equal(AgentNames.LMStudio, result.Agent);
        Assert.Equal("http://override/v1", result.Agents.LMStudio.BaseUrl);
        Assert.Equal("override-key", result.Agents.LMStudio.ApiKey);
        Assert.Equal("override-model", result.Agents.LMStudio.DefaultModel);

        // Unsloth's settings, unrelated to the LMStudio-targeted override, must be untouched.
        Assert.Equal(original.Agents.Unsloth.BaseUrl, result.Agents.Unsloth.BaseUrl);
        Assert.Equal(original.Agents.Unsloth.ApiKey, result.Agents.Unsloth.ApiKey);
        Assert.Equal(original.Agents.Unsloth.DefaultModel, result.Agents.Unsloth.DefaultModel);
    }

    [Fact]
    public void ApplyOverrides_WithoutAgentOverride_DefaultsToUnsloth_LeavingLMStudioUntouched()
    {
        var original = Original();
        original.Agents = original.Agents with
        {
            LMStudio = new LMStudioAgentOptions { BaseUrl = "http://original-lmstudio/v1", ApiKey = "orig-key", DefaultModel = "orig-model" },
        };

        var result = original.ApplyOverrides(baseUrl: "http://override/v1", apiKey: "override-key", defaultModel: "override-model");

        Assert.Equal("http://override/v1", result.Agents.Unsloth.BaseUrl);
        Assert.Equal(original.Agents.LMStudio.BaseUrl, result.Agents.LMStudio.BaseUrl);
        Assert.Equal(original.Agents.LMStudio.ApiKey, result.Agents.LMStudio.ApiKey);
        Assert.Equal(original.Agents.LMStudio.DefaultModel, result.Agents.LMStudio.DefaultModel);
    }

    [Theory]
    [InlineData("LMStudio")]
    [InlineData("lmstudio")]
    [InlineData("LMSTUDIO")]
    public void ApplyOverrides_MatchesAgentCaseInsensitively(string agentValue)
    {
        var result = new RedStarOptions().ApplyOverrides(agent: agentValue, defaultModel: "m");

        Assert.Equal("m", result.Agents.LMStudio.DefaultModel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ApplyOverrides_KeepsOriginalAgent_WhenAgentOverrideIsBlank(string? blankAgent)
    {
        var original = Original();
        original.Agent = AgentNames.LMStudio;

        var result = original.ApplyOverrides(agent: blankAgent);

        Assert.Equal(AgentNames.LMStudio, result.Agent);
    }

    [Fact]
    public void ApplyOverrides_DoesNotMutateTheOriginalInstance()
    {
        var original = Original();

        original.ApplyOverrides(baseUrl: "http://override/v1", apiKey: "override-key", defaultModel: "override-model");

        Assert.Equal("http://original/v1", original.Agents.Unsloth.BaseUrl);
        Assert.Equal("original-key", original.Agents.Unsloth.ApiKey);
        Assert.Equal("original-model", original.Agents.Unsloth.DefaultModel);
    }

    [Fact]
    public void ClaudeCodeAgentOptions_HasExpectedDefaults()
    {
        var claudeCode = new RedStarOptions().Agents.ClaudeCode;

        Assert.Equal("claude", claudeCode.CliPath);
        Assert.Equal(ClaudeCodeAuthModes.CliLogin, claudeCode.AuthMode);
        Assert.Equal("", claudeCode.ApiKey);
        Assert.False(claudeCode.Bare);
        Assert.Equal("", claudeCode.DefaultModel);
        Assert.Equal(ClaudeCodeProcessModes.PerTurn, claudeCode.ProcessMode);
        Assert.Equal("", claudeCode.WorkingDirectory);
        Assert.Empty(claudeCode.AllowedTools);
        Assert.Empty(claudeCode.DisallowedTools);
        Assert.Equal("", claudeCode.PermissionMode);
        Assert.Null(claudeCode.MaxBudgetUsd);
    }

    [Fact]
    public void ApplyOverrides_WithAgentClaudeCode_OverridesApiKeyAndDefaultModel_LeavingUnslothUntouched()
    {
        var original = Original();

        var result = original.ApplyOverrides(agent: AgentNames.ClaudeCode, apiKey: "cc-key", defaultModel: "sonnet");

        Assert.Equal(AgentNames.ClaudeCode, result.Agent);
        Assert.Equal("cc-key", result.Agents.ClaudeCode.ApiKey);
        Assert.Equal("sonnet", result.Agents.ClaudeCode.DefaultModel);
        Assert.Equal(original.Agents.Unsloth.BaseUrl, result.Agents.Unsloth.BaseUrl);
        Assert.Equal(original.Agents.Unsloth.ApiKey, result.Agents.Unsloth.ApiKey);
    }

    /// <summary>BaseUrl has no meaning for ClaudeCode (a subprocess agent, not HTTP) -- passing it must be a
    /// harmless no-op, not an error, since RedStar.Cli's RedStarOptionsFactory always passes whatever
    /// --endpoint was given regardless of which agent ends up active.</summary>
    [Fact]
    public void ApplyOverrides_WithAgentClaudeCode_IgnoresBaseUrlOverride()
    {
        var result = new RedStarOptions().ApplyOverrides(agent: AgentNames.ClaudeCode, baseUrl: "http://ignored/v1", apiKey: "k");

        Assert.Equal("k", result.Agents.ClaudeCode.ApiKey);
    }

    [Fact]
    public void ApplyOverrides_WithAgentClaudeCode_AppliesClaudeCodeOverrides()
    {
        var claudeCode = new ClaudeCodeOverrides(
            CliPath: "/usr/local/bin/claude",
            AuthMode: ClaudeCodeAuthModes.ApiKey,
            Bare: true,
            ProcessMode: ClaudeCodeProcessModes.LongLived,
            WorkingDirectory: "/work",
            AllowedTools: ["Read", "Grep"],
            DisallowedTools: ["Bash"],
            PermissionMode: "acceptEdits",
            MaxBudgetUsd: 5.0);

        var result = new RedStarOptions().ApplyOverrides(agent: AgentNames.ClaudeCode, claudeCode: claudeCode);

        Assert.Equal(AgentNames.ClaudeCode, result.Agent);
        Assert.Equal("/usr/local/bin/claude", result.Agents.ClaudeCode.CliPath);
        Assert.Equal(ClaudeCodeAuthModes.ApiKey, result.Agents.ClaudeCode.AuthMode);
        Assert.True(result.Agents.ClaudeCode.Bare);
        Assert.Equal(ClaudeCodeProcessModes.LongLived, result.Agents.ClaudeCode.ProcessMode);
        Assert.Equal("/work", result.Agents.ClaudeCode.WorkingDirectory);
        Assert.Equal(["Read", "Grep"], result.Agents.ClaudeCode.AllowedTools);
        Assert.Equal(["Bash"], result.Agents.ClaudeCode.DisallowedTools);
        Assert.Equal("acceptEdits", result.Agents.ClaudeCode.PermissionMode);
        Assert.Equal(5.0, result.Agents.ClaudeCode.MaxBudgetUsd);
    }

    [Fact]
    public void ApplyOverrides_WithAgentClaudeCode_PartialClaudeCodeOverride_LeavesOthersUntouched()
    {
        var original = new RedStarOptions();
        original.Agents = original.Agents with
        {
            ClaudeCode = original.Agents.ClaudeCode with
            {
                CliPath = "original-claude",
                ProcessMode = ClaudeCodeProcessModes.PerTurn,
                AllowedTools = ["Read"],
            },
        };

        var result = original.ApplyOverrides(
            agent: AgentNames.ClaudeCode, claudeCode: new ClaudeCodeOverrides(ProcessMode: ClaudeCodeProcessModes.LongLived));

        Assert.Equal("original-claude", result.Agents.ClaudeCode.CliPath);
        Assert.Equal(ClaudeCodeProcessModes.LongLived, result.Agents.ClaudeCode.ProcessMode);
        Assert.Equal(["Read"], result.Agents.ClaudeCode.AllowedTools);
    }

    [Fact]
    public void ApplyOverrides_WithAgentClaudeCode_KeepsOriginalClaudeCodeSettings_WhenOverridesAreNull()
    {
        var original = new RedStarOptions();
        original.Agents = original.Agents with
        {
            ClaudeCode = original.Agents.ClaudeCode with { CliPath = "original-claude", Bare = true },
        };

        var result = original.ApplyOverrides(agent: AgentNames.ClaudeCode, claudeCode: null);

        Assert.Equal("original-claude", result.Agents.ClaudeCode.CliPath);
        Assert.True(result.Agents.ClaudeCode.Bare);
    }

    /// <summary>Proves <c>Bare: false</c> is a real, applied override distinct from "not passed" -- unlike
    /// the string/list fields (blank/empty means "no override"), a bare <c>bool?</c> can carry an explicit
    /// <c>false</c> that must still win over an original <c>true</c>.</summary>
    [Fact]
    public void ApplyOverrides_WithAgentClaudeCode_ExplicitFalseBare_OverridesOriginalTrue()
    {
        var original = new RedStarOptions();
        original.Agents = original.Agents with { ClaudeCode = original.Agents.ClaudeCode with { Bare = true } };

        var result = original.ApplyOverrides(agent: AgentNames.ClaudeCode, claudeCode: new ClaudeCodeOverrides(Bare: false));

        Assert.False(result.Agents.ClaudeCode.Bare);
    }

    [Theory]
    [InlineData("ClaudeCode")]
    [InlineData("claudecode")]
    [InlineData("CLAUDECODE")]
    public void ApplyOverrides_MatchesClaudeCodeAgentCaseInsensitively(string agentValue)
    {
        var result = new RedStarOptions().ApplyOverrides(agent: agentValue, defaultModel: "sonnet");

        Assert.Equal("sonnet", result.Agents.ClaudeCode.DefaultModel);
    }

    [Fact]
    public void ClaudeCodeOverrides_HasAny_IsFalse_WhenNoFieldIsSet() =>
        Assert.False(new ClaudeCodeOverrides().HasAny);

    [Theory]
    [InlineData("PerTurn")]
    public void ClaudeCodeOverrides_HasAny_IsTrue_WhenProcessModeIsSet(string processMode) =>
        Assert.True(new ClaudeCodeOverrides(ProcessMode: processMode).HasAny);

    [Fact]
    public void ClaudeCodeOverrides_HasAny_IsTrue_WhenBareIsExplicitlyFalse() =>
        Assert.True(new ClaudeCodeOverrides(Bare: false).HasAny);

    [Fact]
    public void ClaudeCodeOverrides_HasAny_IsTrue_WhenMaxBudgetUsdIsSet() =>
        Assert.True(new ClaudeCodeOverrides(MaxBudgetUsd: 1.0).HasAny);

    [Fact]
    public void ClaudeCodeOverrides_HasAny_IsTrue_WhenAllowedToolsIsNonEmpty() =>
        Assert.True(new ClaudeCodeOverrides(AllowedTools: ["Read"]).HasAny);

    [Fact]
    public void ClaudeCodeOverrides_HasAny_IsFalse_WhenAllowedToolsIsEmpty() =>
        Assert.False(new ClaudeCodeOverrides(AllowedTools: []).HasAny);
}
