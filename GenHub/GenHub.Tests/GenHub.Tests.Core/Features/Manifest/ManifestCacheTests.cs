using System.Linq;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
using Xunit;

namespace GenHub.Tests.Core.Features.Manifest;

/// <summary>
/// Unit tests for the <see cref="ManifestCache"/> class.
/// </summary>
public class ManifestCacheTests
{
    /// <summary>
    /// The manifest cache under test.
    /// </summary>
    private readonly ManifestCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestCacheTests"/> class.
    /// </summary>
    public ManifestCacheTests()
    {
        _cache = new ManifestCache();
    }

    /// <summary>
    /// Tests that GetManifest returns null when manifest is not found.
    /// </summary>
    [Fact]
    public void GetManifest_ReturnsNull_WhenManifestNotFound()
    {
        // Act
        var result = _cache.GetManifest("non-existent");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that AddOrUpdateManifest adds a new manifest successfully.
    /// </summary>
    [Fact]
    public void AddOrUpdateManifest_AddsNewManifest_Successfully()
    {
        // Arrange
        var manifest = new ContentManifest { Id = "test-id", Name = "Test Manifest" };

        // Act
        _cache.AddOrUpdateManifest(manifest);
        var result = _cache.GetManifest("test-id");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-id", result.Id);
        Assert.Equal("Test Manifest", result.Name);
    }

    /// <summary>
    /// Tests that AddOrUpdateManifest updates an existing manifest successfully.
    /// </summary>
    [Fact]
    public void AddOrUpdateManifest_UpdatesExistingManifest_Successfully()
    {
        // Arrange
        var originalManifest = new ContentManifest { Id = "test-id", Name = "Original" };
        var updatedManifest = new ContentManifest { Id = "test-id", Name = "Updated" };

        // Act
        _cache.AddOrUpdateManifest(originalManifest);
        _cache.AddOrUpdateManifest(updatedManifest);
        var result = _cache.GetManifest("test-id");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-id", result.Id);
        Assert.Equal("Updated", result.Name);
    }

    /// <summary>
    /// Tests that GetAllManifests returns all cached manifests.
    /// </summary>
    [Fact]
    public void GetAllManifests_ReturnsAllCachedManifests()
    {
        // Arrange
        var manifest1 = new ContentManifest { Id = "id1", Name = "Manifest 1" };
        var manifest2 = new ContentManifest { Id = "id2", Name = "Manifest 2" };

        // Act
        _cache.AddOrUpdateManifest(manifest1);
        _cache.AddOrUpdateManifest(manifest2);
        var results = _cache.GetAllManifests().ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, m => m.Id == "id1");
        Assert.Contains(results, m => m.Id == "id2");
    }

    /// <summary>
    /// Tests that GetAllManifests returns empty collection when no manifests are added.
    /// </summary>
    [Fact]
    public void GetAllManifests_ReturnsEmpty_WhenNoManifestsAdded()
    {
        // Act
        var results = _cache.GetAllManifests().ToList();

        // Assert
        Assert.Empty(results);
    }
}
