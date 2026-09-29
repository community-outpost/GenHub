using GenHub.Core.Constants;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Regression tests for Publisher Studio review findings.
/// </summary>
public class PublisherStudioReviewThreadTests
{
    /// <summary>
    /// Verifies the variant axis selector exposes every known axis plus a custom option.
    /// </summary>
    [Fact]
    public void VariantAxisSelector_BuildOptions_ContainsAllKnownVariantAxes()
    {
        var selector = new VariantAxisSelector(null);

        foreach (var axis in CatalogConstants.KnownVariantAxes)
        {
            Assert.Contains(selector.Options, o => !o.IsCustom && o.Value == axis);
        }

        var customOption = selector.Options.FirstOrDefault(o => o.IsCustom);
        Assert.NotNull(customOption);
    }

    /// <summary>
    /// Verifies a custom axis value surfaces through the effective value and description.
    /// </summary>
    [Fact]
    public void VariantAxisSelector_EffectiveValue_ReturnsCustomWhenCustomSelected()
    {
        var selector = new VariantAxisSelector(null);
        selector.SetValue("my-custom-axis");

        Assert.True(selector.ShowCustomValue);
        Assert.Equal("my-custom-axis", selector.EffectiveValue);
        Assert.NotNull(selector.SelectedDescription);
    }

    /// <summary>
    /// Verifies bundle content requires at least one selected component before creation.
    /// </summary>
    [Fact]
    public void AddContentDialog_BundleType_RequiresAtLeastOneComponent()
    {
        using var vm = new AddContentDialogViewModel(_ => { }, null, null);
        vm.SelectedContentType = ContentType.ContentBundle;
        vm.ContentId = "my-bundle";
        vm.ContentName = "My Bundle";
        vm.Description = "Bundle Description";

        Assert.DoesNotContain(vm.BundleComponentOptions, o => o.IsSelected);

        vm.CreateContentCommand.Execute(null);

        Assert.False(vm.IsValid);
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("bundle component is required", vm.ValidationError, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies switching away from the bundle type restores the initial release option.
    /// </summary>
    [Fact]
    public void AddContentDialog_SwitchingAwayFromBundle_RestoresIncludeInitialRelease()
    {
        using var vm = new AddContentDialogViewModel(_ => { }, null, null);
        vm.SelectedContentType = ContentType.ContentBundle;
        Assert.False(vm.IncludeInitialRelease);

        vm.SelectedContentType = ContentType.Mod;
        Assert.True(vm.IncludeInitialRelease);
    }
}
