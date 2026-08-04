using RedStar.Base;

namespace RedStar.UnitTest;

public class ModelSelectorTests
{
    private static readonly ModelInfo Loaded = new("loaded-model", Loaded: true);
    private static readonly ModelInfo NotLoaded = new("other-model", Loaded: false);

    [Fact]
    public void SelectDefault_ReturnsConfiguredModel_WhenPresentInList()
    {
        var models = new[] { NotLoaded, Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "other-model");

        Assert.Same(NotLoaded, result);
    }

    [Fact]
    public void SelectDefault_TrustsConfiguredModel_WhenNotInServerList()
    {
        var models = new[] { Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "not-yet-downloaded");

        Assert.NotNull(result);
        Assert.Equal("not-yet-downloaded", result.Id);
        Assert.False(result.Loaded);
    }

    [Fact]
    public void SelectDefault_ReturnsLoadedModel_WhenNoConfiguredDefault()
    {
        var models = new[] { NotLoaded, Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.Same(Loaded, result);
    }

    [Fact]
    public void SelectDefault_ReturnsFirstModel_WhenNoneLoadedAndNoConfiguredDefault()
    {
        var models = new[] { NotLoaded, new ModelInfo("another", Loaded: false) };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.Same(NotLoaded, result);
    }

    [Fact]
    public void SelectDefault_ReturnsNull_WhenListIsEmptyAndNoConfiguredDefault()
    {
        var result = ModelSelector.SelectDefault([], configuredDefault: null);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SelectDefault_TreatsBlankConfiguredDefault_AsUnset(string? configuredDefault)
    {
        var models = new[] { NotLoaded, Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault);

        Assert.Same(Loaded, result);
    }

    [Fact]
    public void SelectDefault_Throws_WhenModelsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ModelSelector.SelectDefault(null!, "any"));
    }
}
