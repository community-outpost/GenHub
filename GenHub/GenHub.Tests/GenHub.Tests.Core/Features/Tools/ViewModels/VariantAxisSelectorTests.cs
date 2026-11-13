using GenHub.Core.Constants;
using GenHub.Features.Tools.ViewModels.Dialogs;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for <see cref="VariantAxisSelector"/>.
/// </summary>
public sealed class VariantAxisSelectorTests
{
    /// <summary>
    /// A blank initial value must select the none option with a null effective value.
    /// </summary>
    /// <param name="initial">The blank initial value.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetValue_Blank_SelectsNone(string? initial)
    {
        var selector = new VariantAxisSelector(null, initial);

        Assert.Null(selector.EffectiveValue);
        Assert.False(selector.ShowCustomValue);
        Assert.NotNull(selector.SelectedDescription);
    }

    /// <summary>
    /// A well-known axis must select its documented option.
    /// </summary>
    [Fact]
    public void SetValue_KnownAxis_SelectsDocumentedOption()
    {
        var selector = new VariantAxisSelector(null);

        selector.SetValue(CatalogConstants.ResolutionVariantAxis);

        Assert.Equal(CatalogConstants.ResolutionVariantAxis, selector.EffectiveValue);
        Assert.False(selector.ShowCustomValue);
        Assert.Contains("720p", selector.SelectedDescription);
    }

    /// <summary>
    /// An unknown axis must select the custom option and preserve the free-text value.
    /// </summary>
    [Fact]
    public void SetValue_UnknownAxis_SelectsCustom()
    {
        var selector = new VariantAxisSelector(null);

        selector.SetValue("compatibility");

        Assert.True(selector.ShowCustomValue);
        Assert.Equal("compatibility", selector.EffectiveValue);
        Assert.Equal("compatibility", selector.CustomValue);
    }

    /// <summary>
    /// Clearing the custom input must yield a null effective value.
    /// </summary>
    [Fact]
    public void CustomValue_Cleared_YieldsNull()
    {
        var selector = new VariantAxisSelector(null, "compatibility");

        selector.CustomValue = "   ";

        Assert.Null(selector.EffectiveValue);
    }
}
