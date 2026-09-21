using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.ViewModels;
using System.Collections.ObjectModel;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for cloud hosting asset discovery, categorization, filtering, and project loading.
/// </summary>
public class PublisherStudioHostingDiscoveryAndLoadTests
{
    /// <summary>
    /// Tests that <see cref="HostingConstants.IsPublisherDefinitionFileName"/> properly identifies publisher definition files.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <param name="expected">The expected classification result.</param>
    [Theory]
    [InlineData("publisher.json", true)]
    [InlineData("publisher-prod.json", true)]
    [InlineData("my-publisher.json", true)]
    [InlineData("definition.json", true)]
    [InlineData("publisher_definitions.json", true)]
    [InlineData("catalog.json", false)]
    [InlineData("catalog-main.json", false)]
    [InlineData("game-patch.zip", false)]
    [InlineData(null, false)]
    public void IsPublisherDefinitionFileName_ClassifiesCorrectly(string? fileName, bool expected)
    {
        var result = HostingConstants.IsPublisherDefinitionFileName(fileName);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that <see cref="HostingConstants.IsCatalogFileName"/> properly identifies catalog manifest files.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <param name="expected">The expected classification result.</param>
    [Theory]
    [InlineData("catalog.json", true)]
    [InlineData("catalog-main.json", true)]
    [InlineData("catalog-1.json", true)]
    [InlineData("publisher.json", false)]
    [InlineData("definition.json", false)]
    [InlineData("file.zip", false)]
    [InlineData(null, false)]
    public void IsCatalogFileName_ClassifiesCorrectly(string? fileName, bool expected)
    {
        var result = HostingConstants.IsCatalogFileName(fileName);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that <see cref="HostedAssetItemViewModel"/> configured as a Definition sets expected UI flags.
    /// </summary>
    [Fact]
    public void HostedAssetItemViewModel_DefinitionKind_SetsCorrectProperties()
    {
        var vm = new HostedAssetItemViewModel
        {
            Name = "publisher.json",
            AssetKind = HostedAssetKind.Definition,
            Url = "https://example.com/publisher.json",
            CanLoadToProject = true,
        };

        Assert.True(vm.IsDefinition);
        Assert.False(vm.IsCatalog);
        Assert.False(vm.IsArtifact);
        Assert.True(vm.CanLoadToProject);
        Assert.False(vm.CanAddToCatalog);
    }

    /// <summary>
    /// Tests that <see cref="HostedAssetItemViewModel"/> configured as a Catalog sets expected UI flags.
    /// </summary>
    [Fact]
    public void HostedAssetItemViewModel_CatalogKind_SetsCorrectProperties()
    {
        var vm = new HostedAssetItemViewModel
        {
            Name = "catalog-5.json",
            AssetKind = HostedAssetKind.Catalog,
            Url = "https://example.com/catalog-5.json",
            CanLoadToProject = true,
        };

        Assert.False(vm.IsDefinition);
        Assert.True(vm.IsCatalog);
        Assert.False(vm.IsArtifact);
        Assert.True(vm.CanLoadToProject);
        Assert.False(vm.CanAddToCatalog);
    }

    /// <summary>
    /// Tests that <see cref="HostedAssetItemViewModel"/> configured as an Artifact sets expected UI flags.
    /// </summary>
    [Fact]
    public void HostedAssetItemViewModel_ArtifactKind_SetsCorrectProperties()
    {
        var vm = new HostedAssetItemViewModel
        {
            Name = "mod-v1.zip",
            AssetKind = HostedAssetKind.Artifact,
            Url = "https://example.com/mod-v1.zip",
            CanAddToCatalog = true,
        };

        Assert.False(vm.IsDefinition);
        Assert.False(vm.IsCatalog);
        Assert.True(vm.IsArtifact);
        Assert.False(vm.CanLoadToProject);
        Assert.True(vm.CanAddToCatalog);
    }

    /// <summary>
    /// Tests that <see cref="HostingState"/> preserves and exposes multiple discovered definition files.
    /// </summary>
    [Fact]
    public void HostingState_DefinitionsList_TracksMultipleDefinitions()
    {
        var state = new HostingState
        {
            ProviderId = "dropbox",
            Definition = new HostedFileInfo { FileName = "publisher.json", Url = "https://dropbox.com/p1" },
            Definitions =
            [
                new HostedFileInfo { FileName = "publisher.json", Url = "https://dropbox.com/p1" },
                new HostedFileInfo { FileName = "publisher-beta.json", Url = "https://dropbox.com/p2" },
                new HostedFileInfo { FileName = "publisher-legacy.json", Url = "https://dropbox.com/p3" },
            ],
        };

        Assert.Equal(3, state.Definitions.Count);
        Assert.Equal("publisher.json", state.Definition.FileName);
        Assert.Contains(state.Definitions, d => d.FileName == "publisher-beta.json");
    }
}
