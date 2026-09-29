using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels.Dialogs;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests proving bundle-mode releases never carry variant data.
/// Bundle-mode artifacts install together as one package, so any variant
/// selection is cleared when bundling is enabled and on save.
/// </summary>
public sealed class ReleaseDialogBundleModeTests
{
    /// <summary>
    /// Switching the release dialog to bundle mode must strip variant fields
    /// from staged artifacts.
    /// </summary>
    [Fact]
    public void ReleaseDialog_ToggleToBundle_StripsArtifactVariants()
    {
        var vm = new AddReleaseDialogViewModel(
            new CatalogContentItem { Id = "mod", Name = "Mod" },
            new PublisherCatalog(),
            _ => { },
            Mock.Of<IPublisherStudioDialogService>());

        vm.IsVariantsMode = true;
        vm.Artifacts.Add(new ReleaseArtifact
        {
            Filename = "mod-1080p.zip",
            VariantAxis = "resolution",
            Variant = "1080p",
            IsDefaultVariant = true,
        });

        vm.BundleArtifacts = true;

        var artifact = Assert.Single(vm.Artifacts);
        Assert.Null(artifact.VariantAxis);
        Assert.Null(artifact.Variant);
        Assert.False(artifact.IsDefaultVariant);
        Assert.False(vm.IsVariantsMode);
    }

    /// <summary>
    /// Saving a bundle-mode release must strip variant fields even when the
    /// mode toggle never fired, such as legacy variant-carrying artifacts.
    /// </summary>
    [Fact]
    public void ReleaseDialog_CreateRelease_BundleMode_StripsArtifactVariants()
    {
        ContentRelease? created = null;
        var vm = new AddReleaseDialogViewModel(
            new CatalogContentItem { Id = "mod", Name = "Mod" },
            new PublisherCatalog(),
            r => created = r,
            Mock.Of<IPublisherStudioDialogService>());

        vm.Artifacts.Add(new ReleaseArtifact
        {
            Filename = "mod.zip",
            DownloadUrl = "https://example.com/mod.zip",
            VariantAxis = "resolution",
            Variant = "1080p",
            IsDefaultVariant = true,
        });

        vm.CreateReleaseCommand.Execute(null);

        Assert.NotNull(created);
        Assert.True(created.BundleArtifacts);
        var artifact = Assert.Single(created.Artifacts);
        Assert.Null(artifact.VariantAxis);
        Assert.Null(artifact.Variant);
        Assert.False(artifact.IsDefaultVariant);
    }

    /// <summary>
    /// Switching the content dialog initial release to bundle mode must strip
    /// variant fields from its artifacts.
    /// </summary>
    [Fact]
    public void ContentDialog_ToggleToBundle_StripsReleaseArtifacts()
    {
        using var vm = new AddContentDialogViewModel(_ => { });

        vm.IsVariantsMode = true;
        vm.ReleaseArtifacts.Add(new ReleaseArtifact
        {
            Filename = "mod-1080p.zip",
            VariantAxis = "resolution",
            Variant = "1080p",
            IsDefaultVariant = true,
        });

        vm.BundleArtifacts = true;

        var artifact = Assert.Single(vm.ReleaseArtifacts);
        Assert.Null(artifact.VariantAxis);
        Assert.Null(artifact.Variant);
        Assert.False(artifact.IsDefaultVariant);
    }

    /// <summary>
    /// Creating content with a bundle-mode initial release must strip variant
    /// fields from its artifacts on save.
    /// </summary>
    [Fact]
    public void ContentDialog_CreateContent_BundleMode_StripsReleaseArtifacts()
    {
        CatalogContentItem? created = null;
        using var vm = new AddContentDialogViewModel(item => created = item);
        vm.ContentId = "test-mod";
        vm.ContentName = "Test Mod";
        vm.Description = "A test mod description.";
        vm.ReleaseArtifacts.Add(new ReleaseArtifact
        {
            Filename = "mod.zip",
            DownloadUrl = "https://example.com/mod.zip",
            Size = 12,
            Sha256 = "abc",
            IsPrimary = true,
            VariantAxis = "resolution",
            Variant = "1080p",
            IsDefaultVariant = true,
        });

        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(created);
        var release = Assert.Single(created.Releases);
        Assert.True(release.BundleArtifacts);
        var artifact = Assert.Single(release.Artifacts);
        Assert.Null(artifact.VariantAxis);
        Assert.Null(artifact.Variant);
        Assert.False(artifact.IsDefaultVariant);
    }
}
