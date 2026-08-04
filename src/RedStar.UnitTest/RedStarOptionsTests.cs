using RedStar.Base;

namespace RedStar.UnitTest;

public class RedStarOptionsTests
{
    private static RedStarOptions Original() => new()
    {
        BaseUrl = "http://original/v1",
        ApiKey = "original-key",
        DefaultModel = "original-model",
    };

    [Fact]
    public void ApplyOverrides_AppliesAllNonBlankValues()
    {
        var result = Original().ApplyOverrides(
            baseUrl: "http://override/v1",
            apiKey: "override-key",
            defaultModel: "override-model");

        Assert.Equal("http://override/v1", result.BaseUrl);
        Assert.Equal("override-key", result.ApiKey);
        Assert.Equal("override-model", result.DefaultModel);
    }

    [Fact]
    public void ApplyOverrides_KeepsOriginalValues_WhenOverridesAreNull()
    {
        var original = Original();

        var result = original.ApplyOverrides(baseUrl: null, apiKey: null, defaultModel: null);

        Assert.Equal(original.BaseUrl, result.BaseUrl);
        Assert.Equal(original.ApiKey, result.ApiKey);
        Assert.Equal(original.DefaultModel, result.DefaultModel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyOverrides_KeepsOriginalValues_WhenOverridesAreBlank(string blank)
    {
        var original = Original();

        var result = original.ApplyOverrides(baseUrl: blank, apiKey: blank, defaultModel: blank);

        Assert.Equal(original.BaseUrl, result.BaseUrl);
        Assert.Equal(original.ApiKey, result.ApiKey);
        Assert.Equal(original.DefaultModel, result.DefaultModel);
    }

    [Fact]
    public void ApplyOverrides_AppliesPartialOverride_LeavingOthersUntouched()
    {
        var original = Original();

        var result = original.ApplyOverrides(defaultModel: "just-the-model");

        Assert.Equal(original.BaseUrl, result.BaseUrl);
        Assert.Equal(original.ApiKey, result.ApiKey);
        Assert.Equal("just-the-model", result.DefaultModel);
    }

    [Fact]
    public void ApplyOverrides_DoesNotMutateTheOriginalInstance()
    {
        var original = Original();

        original.ApplyOverrides(baseUrl: "http://override/v1", apiKey: "override-key", defaultModel: "override-model");

        Assert.Equal("http://original/v1", original.BaseUrl);
        Assert.Equal("original-key", original.ApiKey);
        Assert.Equal("original-model", original.DefaultModel);
    }
}
