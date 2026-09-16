using GenHub.Core.Constants;
using GenHub.Core.Models.Manifest;
using System.Text.Json;
using Xunit;

namespace GenHub.Tests.Core.Features.Manifest;

/// <summary>
/// Unit tests for <see cref="ContentManifest"/>.
/// </summary>
public class ContentManifestTests
{
    /// <summary>
    /// Tests that the copy constructor handles manifests with null collection properties without throwing.
    /// </summary>
    [Fact]
    public void CopyConstructor_WithNullCollections_DoesNotThrow()
    {
        var original = new ContentManifest
        {
            Id = ManifestId.Create("1.104.ea.gameclient.generals"),
            Name = "Generals",
            Dependencies = null!,
            ContentReferences = null!,
            KnownAddons = null!,
            Files = null!,
            Variants = null!,
            RequiredDirectories = null!,
        };

        var copy = new ContentManifest(original);

        Assert.NotNull(copy.Dependencies);
        Assert.Empty(copy.Dependencies);
        Assert.NotNull(copy.ContentReferences);
        Assert.Empty(copy.ContentReferences);
        Assert.NotNull(copy.KnownAddons);
        Assert.Empty(copy.KnownAddons);
        Assert.NotNull(copy.Files);
        Assert.Empty(copy.Files);
        Assert.NotNull(copy.Variants);
        Assert.Empty(copy.Variants);
        Assert.NotNull(copy.RequiredDirectories);
        Assert.Empty(copy.RequiredDirectories);
    }

    /// <summary>
    /// Tests that SchemaVersion defaults to default manifest format version and ManifestVersion behaves as an alias.
    /// </summary>
    [Fact]
    public void SchemaVersion_And_ManifestVersionAlias_AreSynchronized()
    {
        var manifest = new ContentManifest();
        Assert.Equal(ManifestConstants.DefaultManifestVersion, manifest.SchemaVersion);
        Assert.Equal(ManifestConstants.DefaultManifestVersion, manifest.ManifestVersion);

        manifest.SchemaVersion = "2";
        Assert.Equal("2", manifest.ManifestVersion);

        manifest.ManifestVersion = "3";
        Assert.Equal("3", manifest.SchemaVersion);
    }

    /// <summary>
    /// Tests that copy constructor and Clone preserve SchemaVersion.
    /// </summary>
    [Fact]
    public void CopyConstructor_And_Clone_PreserveSchemaVersion()
    {
        var original = new ContentManifest
        {
            Id = ManifestId.Create("1.104.ea.gameclient.generals"),
            Name = "Generals",
            SchemaVersion = "2",
        };

        var copy = new ContentManifest(original);
        Assert.Equal("2", copy.SchemaVersion);
        Assert.Equal("2", copy.ManifestVersion);

        var cloned = original.Clone();
        Assert.Equal("2", cloned.SchemaVersion);
        Assert.Equal("2", cloned.ManifestVersion);
    }

    /// <summary>
    /// Tests that ContentManifest serializes SchemaVersion with the JSON property name ManifestVersion
    /// for backward compatibility with existing manifest storage.
    /// </summary>
    [Fact]
    public void Serialization_MaintainsManifestVersionJsonProperty()
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.104.ea.gameclient.generals"),
            Name = "Generals",
            SchemaVersion = "2",
        };

        var json = JsonSerializer.Serialize(manifest);
        Assert.Contains("\"ManifestVersion\":\"2\"", json);

        var deserialized = JsonSerializer.Deserialize<ContentManifest>(json);
        Assert.NotNull(deserialized);
        Assert.Equal("2", deserialized.SchemaVersion);
        Assert.Equal("2", deserialized.ManifestVersion);
    }
}
