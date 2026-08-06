using RedStar.Base;

namespace RedStar.UnitTest;

public class ModelSelectorTests
{
    private static readonly ModelInfo Loaded = new("loaded-model", Loaded: true);
    private static readonly ModelInfo OtherLoaded = new("other-loaded-model", Loaded: true);
    private static readonly ModelInfo NotLoaded = new("not-loaded-model", Loaded: false);

    [Fact]
    public void SelectDefault_Fails_WhenNoModelsAreLoaded()
    {
        var models = new[] { NotLoaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.False(result.Succeeded);
        Assert.Null(result.Model);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void SelectDefault_Fails_WhenModelListIsEmpty()
    {
        var result = ModelSelector.SelectDefault([], configuredDefault: null);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void SelectDefault_ReturnsConfiguredModel_WhenItIsLoaded()
    {
        var models = new[] { NotLoaded, Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "loaded-model");

        Assert.True(result.Succeeded);
        Assert.Same(Loaded, result.Model);
        Assert.Equal(ModelSelectionSource.Explicit, result.Source);
        Assert.Null(result.InfoMessage);
    }

    /// <summary>
    /// The configured model not being loaded is a hard failure even though a *different* model is
    /// loaded -- silently substituting that other model would be misleading (a user who typo'd or
    /// forgot to load their configured model would unknowingly end up talking to a different one).
    /// </summary>
    [Fact]
    public void SelectDefault_Fails_WhenConfiguredModelIsNotLoaded_EvenThoughAnotherModelIsLoaded()
    {
        var models = new[] { Loaded, NotLoaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "not-loaded-model");

        Assert.False(result.Succeeded);
        Assert.Null(result.Model);
        Assert.Contains("not-loaded-model", result.ErrorMessage);
        Assert.Contains("loaded-model", result.ErrorMessage);
    }

    [Fact]
    public void SelectDefault_Fails_WhenConfiguredModelIsUnknownToTheServer_EvenThoughAnotherModelIsLoaded()
    {
        var models = new[] { Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "not-yet-downloaded");

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void SelectDefault_ReturnsTheOnlyLoadedModel_WhenNoConfiguredDefault()
    {
        var models = new[] { NotLoaded, Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.True(result.Succeeded);
        Assert.Same(Loaded, result.Model);
        Assert.Equal(ModelSelectionSource.Implicit, result.Source);
        Assert.NotNull(result.InfoMessage);
    }

    [Fact]
    public void SelectDefault_Fails_WhenMultipleModelsAreLoadedAndNoneIsConfigured()
    {
        var models = new[] { Loaded, OtherLoaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void SelectDefault_ReturnsConfiguredModel_WhenMultipleAreLoadedAndItMatchesOne()
    {
        var models = new[] { Loaded, OtherLoaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "other-loaded-model");

        Assert.True(result.Succeeded);
        Assert.Same(OtherLoaded, result.Model);
        Assert.Equal(ModelSelectionSource.Explicit, result.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SelectDefault_TreatsBlankConfiguredDefault_AsUnset(string? configuredDefault)
    {
        var models = new[] { Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault);

        Assert.True(result.Succeeded);
        Assert.Same(Loaded, result.Model);
        Assert.Equal(ModelSelectionSource.Implicit, result.Source);
    }

    [Fact]
    public void SelectDefault_Throws_WhenModelsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ModelSelector.SelectDefault(null!, "any"));
    }
}
