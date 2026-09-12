using GenHub.Features.GameProfiles.Services;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests verifying catalog identity compatibility matching in <see cref="DependencyResolver"/>.
/// </summary>
public sealed class DependencyResolverCatalogIdentityTests
{
    /// <summary>
    /// Tests that exact 5-segment ID matches return true.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_ExactMatch_ReturnsTrue()
    {
        var declared = "1.0.thesuperhackers.gameclient.zerohour".Split('.');
        var acquired = "1.0.thesuperhackers.gameclient.zerohour".Split('.');

        Assert.True(DependencyResolver.HasCompatibleCatalogIdentity(declared, acquired));
    }

    /// <summary>
    /// Tests that hyphen-suffix variant matches return true.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_VariantHyphenSuffix_ReturnsTrue()
    {
        var declared = "1.0.generic-catalog.addon.lemon-controlbar".Split('.');
        var acquired = "1.0.generic-catalog.addon.lemon-controlbar-1080p".Split('.');

        Assert.True(DependencyResolver.HasCompatibleCatalogIdentity(declared, acquired));
    }

    /// <summary>
    /// Tests that mismatched publishers return false.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_CrossPublisher_ReturnsFalse()
    {
        var declared = "1.0.thesuperhackers.gameclient.zerohour".Split('.');
        var acquired = "1.0.communityoutpost.gameclient.zerohour".Split('.');

        Assert.False(DependencyResolver.HasCompatibleCatalogIdentity(declared, acquired));
    }

    /// <summary>
    /// Tests that prefix squatting without hyphen separator returns false.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_PrefixSquatting_ReturnsFalse()
    {
        var declared = "1.0.generic-catalog.mod.mod".Split('.');
        var acquired = "1.0.generic-catalog.mod.modpack".Split('.');

        Assert.False(DependencyResolver.HasCompatibleCatalogIdentity(declared, acquired));
    }

    /// <summary>
    /// Tests that reverse variant prefix matching returns false.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_ReverseVariantPrefix_ReturnsFalse()
    {
        var declared = "1.0.generic-catalog.addon.lemon-controlbar-1080p".Split('.');
        var acquired = "1.0.generic-catalog.addon.lemon-controlbar".Split('.');

        Assert.False(DependencyResolver.HasCompatibleCatalogIdentity(declared, acquired));
    }

    /// <summary>
    /// Tests that non-5-segment IDs return false.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_InvalidSegmentCount_ReturnsFalse()
    {
        var declared = "1.0.mod.item".Split('.');
        var acquired = "1.0.generic-catalog.mod.item".Split('.');

        Assert.False(DependencyResolver.HasCompatibleCatalogIdentity(declared, acquired));
    }

    /// <summary>
    /// Tests that declared wildcard publisher "any" matches acquired publisher.
    /// </summary>
    [Fact]
    public void HasCompatibleCatalogIdentity_AnyPublisherWildcard_ReturnsTrue()
    {
        var declared = "1.0.any.gameclient.zerohour".Split('.');
        var acquired = "1.0.thesuperhackers.gameclient.zerohour".Split('.');

        Assert.True(DependencyResolver.HasCompatibleCatalogIdentity(declared, acquired));
    }

    /// <summary>
    /// Tests that FindVersionIndependentCatalogMatch returns null when installed manifests do not satisfy version constraint.
    /// </summary>
    [Fact]
    public void FindVersionIndependentCatalogMatch_IncompatibleVersion_ReturnsNull()
    {
        var manifests = new[]
        {
            new GenHub.Core.Models.Manifest.ContentManifest
            {
                Id = new GenHub.Core.Models.Manifest.ManifestId("1.0.generic-catalog.mod.example"),
                Version = "1.0.0",
            },
        };

        var dependency = new GenHub.Core.Models.Manifest.ContentDependency
        {
            Id = new GenHub.Core.Models.Manifest.ManifestId("1.0.generic-catalog.mod.example"),
            MinVersion = "2.0.0",
        };

        var result = DependencyResolver.FindVersionIndependentCatalogMatch(
            "1.0.generic-catalog.mod.example",
            dependency,
            manifests);

        Assert.Null(result);
    }

    /// <summary>
    /// Tests that FindVersionIndependentCatalogMatch returns matching manifest when version constraint is satisfied.
    /// </summary>
    [Fact]
    public void FindVersionIndependentCatalogMatch_CompatibleVersion_ReturnsMatch()
    {
        var manifests = new[]
        {
            new GenHub.Core.Models.Manifest.ContentManifest
            {
                Id = new GenHub.Core.Models.Manifest.ManifestId("1.0.generic-catalog.mod.example"),
                Version = "2.1.0",
            },
        };

        var dependency = new GenHub.Core.Models.Manifest.ContentDependency
        {
            Id = new GenHub.Core.Models.Manifest.ManifestId("1.0.generic-catalog.mod.example"),
            MinVersion = "2.0.0",
        };

        var result = DependencyResolver.FindVersionIndependentCatalogMatch(
            "1.0.generic-catalog.mod.example",
            dependency,
            manifests);

        Assert.Equal("1.0.generic-catalog.mod.example", result);
    }
}
