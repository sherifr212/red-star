using RedStar.Base;

namespace RedStar.UnitTest;

public class ModelSelectorTests
{
    private static readonly ModelInfo LoadedA = new("model-a", Loaded: true);
    private static readonly ModelInfo LoadedB = new("model-b", Loaded: true);
    private static readonly ModelInfo NotLoaded = new("model-c", Loaded: false);

    [Fact]
    public void SelectDefault_Fails_WhenNoModelsAreLoaded()
    {
        var models = new[] { NotLoaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Model);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void SelectDefault_Fails_WhenModelListIsEmpty()
    {
        var result = ModelSelector.SelectDefault([], configuredDefault: null);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void SelectDefault_ReturnsTheOnlyLoadedModel_Implicitly_WhenNoConfiguredDefault()
    {
        var models = new[] { NotLoaded, LoadedA };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.True(result.IsSuccess);
        Assert.Same(LoadedA, result.Model);
        Assert.Equal(ModelSelectionSource.Implicit, result.Source);
    }

    [Fact]
    public void SelectDefault_ReturnsTheOnlyLoadedModel_OverridingADifferentConfiguredDefault()
    {
        var models = new[] { LoadedA };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "some-other-model");

        Assert.True(result.IsSuccess);
        Assert.Same(LoadedA, result.Model);
        Assert.Equal(ModelSelectionSource.Implicit, result.Source);
        Assert.Contains("some-other-model", result.Message);
    }

    [Fact]
    public void SelectDefault_ReturnsTheOnlyLoadedModel_WhenItMatchesTheConfiguredDefault()
    {
        var models = new[] { LoadedA };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "model-a");

        Assert.True(result.IsSuccess);
        Assert.Same(LoadedA, result.Model);
        Assert.Equal(ModelSelectionSource.Implicit, result.Source);
    }

    [Fact]
    public void SelectDefault_ReturnsConfiguredModel_WhenMultipleAreLoaded()
    {
        var models = new[] { LoadedA, LoadedB };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "model-b");

        Assert.True(result.IsSuccess);
        Assert.Same(LoadedB, result.Model);
        Assert.Equal(ModelSelectionSource.Explicit, result.Source);
    }

    [Fact]
    public void SelectDefault_Fails_WhenMultipleAreLoadedAndNoDefaultIsConfigured()
    {
        var models = new[] { LoadedA, LoadedB };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void SelectDefault_Fails_WhenMultipleAreLoadedAndConfiguredDefaultIsNotAmongThem()
    {
        var models = new[] { LoadedA, LoadedB, NotLoaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "model-c");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SelectDefault_TreatsBlankConfiguredDefault_AsUnset(string? configuredDefault)
    {
        var models = new[] { LoadedA };

        var result = ModelSelector.SelectDefault(models, configuredDefault);

        Assert.True(result.IsSuccess);
        Assert.Same(LoadedA, result.Model);
        Assert.Equal(ModelSelectionSource.Implicit, result.Source);
    }

    [Fact]
    public void SelectDefault_Throws_WhenModelsIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => ModelSelector.SelectDefault(null!, "any"));
    }
}
