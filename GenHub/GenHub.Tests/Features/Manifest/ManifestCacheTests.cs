using System.Linq;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Manifest;
using Xunit;

namespace GenHub.Tests.Features.Manifest;

public class ManifestCacheTests
{
    private readonly ManifestCache _cache;

    public ManifestCacheTests()
    {
        _cache = new ManifestCache();
    }

    [Fact]
    public void GetManifest_ReturnsNull_WhenManifestNotFound()
    {
        // Act
        var result = _cache.GetManifest("non-existent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void AddOrUpdateManifest_AddsNewManifest_Successfully()
    {
        // Arrange
        var manifest = new GameManifest { Id = "test-id", Name = "Test Manifest" };

        // Act
        _cache.AddOrUpdateManifest(manifest);
        var result = _cache.GetManifest("test-id");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-id", result.Id);
        Assert.Equal("Test Manifest", result.Name);
    }

    [Fact]
    public void AddOrUpdateManifest_UpdatesExistingManifest_Successfully()
    {
        // Arrange
        var originalManifest = new GameManifest { Id = "test-id", Name = "Original" };
        var updatedManifest = new GameManifest { Id = "test-id", Name = "Updated" };

        // Act
        _cache.AddOrUpdateManifest(originalManifest);
        _cache.AddOrUpdateManifest(updatedManifest);
        var result = _cache.GetManifest("test-id");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-id", result.Id);
        Assert.Equal("Updated", result.Name);
    }

    [Fact]
    public void GetAllManifests_ReturnsAllCachedManifests()
    {
        // Arrange
        var manifest1 = new GameManifest { Id = "id1", Name = "Manifest 1" };
        var manifest2 = new GameManifest { Id = "id2", Name = "Manifest 2" };

        // Act
        _cache.AddOrUpdateManifest(manifest1);
        _cache.AddOrUpdateManifest(manifest2);
        var results = _cache.GetAllManifests().ToList();

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Contains(results, m => m.Id == "id1");
        Assert.Contains(results, m => m.Id == "id2");
    }

    [Fact]
    public void GetAllManifests_ReturnsEmpty_WhenNoManifestsAdded()
    {
        // Act
        var results = _cache.GetAllManifests().ToList();

        // Assert
        Assert.Empty(results);
    }
}
