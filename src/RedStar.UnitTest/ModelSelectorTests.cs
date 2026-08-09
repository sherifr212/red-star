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

    [Fact]
    public void SelectDefault_Fails_WhenConfiguredModelIsNotLoaded_AndJitLoadIsNotAllowed()
    {
        var models = new[] { NotLoaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "not-loaded-model", allowJitLoad: false);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void SelectDefault_SucceedsWithPendingJitLoad_WhenConfiguredModelIsKnownButNotLoaded_AndJitLoadIsAllowed()
    {
        var models = new[] { NotLoaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "not-loaded-model", allowJitLoad: true);

        Assert.True(result.Succeeded);
        Assert.Same(NotLoaded, result.Model);
        Assert.Equal(ModelSelectionSource.PendingJitLoad, result.Source);
        Assert.Contains("not-loaded-model", result.InfoMessage);
    }

    [Fact]
    public void SelectDefault_Fails_WhenConfiguredModelIsUnknownToTheServer_EvenWithJitLoadAllowed()
    {
        var models = new[] { Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "not-yet-downloaded", allowJitLoad: true);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void SelectDefault_ReturnsExplicit_NotPendingJitLoad_WhenConfiguredModelIsAlreadyLoaded_AndJitLoadIsAllowed()
    {
        var models = new[] { Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: "loaded-model", allowJitLoad: true);

        Assert.True(result.Succeeded);
        Assert.Equal(ModelSelectionSource.Explicit, result.Source);
    }

    [Fact]
    public void SelectDefault_Fails_WhenNoModelsAreLoadedAndNoDefaultIsConfigured_EvenWithJitLoadAllowed()
    {
        var models = new[] { NotLoaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null, allowJitLoad: true);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void SelectDefault_ExcludesEmbeddingsModel_FromImplicitSingleLoadedSelection()
    {
        var embeddingsModel = new ModelInfo("embed-model", Loaded: true, Type: "embeddings");
        var models = new[] { embeddingsModel, Loaded };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.True(result.Succeeded);
        Assert.Same(Loaded, result.Model);
        Assert.Equal(ModelSelectionSource.Implicit, result.Source);
    }

    [Fact]
    public void SelectDefault_ExcludesEmbeddingsModel_FromAmbiguityCount()
    {
        var embeddingsModel = new ModelInfo("embed-model", Loaded: true, Type: "embeddings");
        var models = new[] { embeddingsModel, Loaded };

        // Only one *chat-capable* model is loaded (the embeddings one doesn't count), so this must
        // succeed implicitly rather than failing as "multiple loaded, ambiguous".
        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void SelectDefault_Fails_WhenOnlyLoadedModelIsAnEmbeddingsModel()
    {
        var embeddingsModel = new ModelInfo("embed-model", Loaded: true, Type: "embeddings");
        var models = new[] { embeddingsModel };

        var result = ModelSelector.SelectDefault(models, configuredDefault: null);

        Assert.False(result.Succeeded);
    }
}
