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
    public void WebSearchEnabled_DefaultsToFalse()
    {
        Assert.False(new RedStarOptions().WebSearchEnabled);
    }

    [Fact]
    public void ApplyOverrides_PreservesWebSearchEnabled_WhichHasNoCliOverride()
    {
        var original = Original();
        original.WebSearchEnabled = true;

        var result = original.ApplyOverrides(
            baseUrl: "http://override/v1", apiKey: "override-key", defaultModel: "override-model");

        Assert.True(result.WebSearchEnabled);
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
    /// any property with no corresponding CLI override (the bug that dropped <see cref="RedStarOptions.WebSearchEnabled"/>).
    /// Walks every public settable property via reflection instead of naming them, so a future property added
    /// to <see cref="RedStarOptions"/> is covered automatically without anyone remembering to update this test.
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
    public void ApplyOverrides_DoesNotMutateTheOriginalInstance()
    {
        var original = Original();

        original.ApplyOverrides(baseUrl: "http://override/v1", apiKey: "override-key", defaultModel: "override-model");

        Assert.Equal("http://original/v1", original.BaseUrl);
        Assert.Equal("original-key", original.ApiKey);
        Assert.Equal("original-model", original.DefaultModel);
    }
}
