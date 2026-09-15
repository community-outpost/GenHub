using System;
using GenHub.Core.Models.Workspace;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Contains unit tests for <see cref="ContentHotswapClassification"/>.
/// </summary>
public class ContentHotswapClassificationTests
{
    /// <summary>
    /// Verifies that IsHotswappable returns expected truth values for all content types.
    /// </summary>
    /// <param name="contentType">The content type under test.</param>
    /// <param name="expected">The expected boolean result.</param>
    [Theory]
    [InlineData(ContentType.Map, true)]
    [InlineData(ContentType.MapPack, true)]
    [InlineData(ContentType.Patch, false)]
    [InlineData(ContentType.Replay, true)]
    [InlineData(ContentType.Mod, false)]
    [InlineData(ContentType.GameClient, false)]
    [InlineData(ContentType.GameInstallation, false)]
    [InlineData(ContentType.Addon, false)]
    [InlineData(ContentType.Executable, false)]
    [InlineData(ContentType.ModdingTool, false)]
    [InlineData(ContentType.Mission, false)]
    [InlineData(ContentType.Skin, false)]
    [InlineData(ContentType.LanguagePack, false)]
    [InlineData(ContentType.ContentBundle, false)]
    [InlineData(ContentType.PublisherReferral, false)]
    [InlineData(ContentType.ContentReferral, false)]
    [InlineData(ContentType.Video, false)]
    [InlineData(ContentType.Screensaver, false)]
    [InlineData(ContentType.UnknownContentType, false)]
    public void IsHotswappable_ReturnsExpectedResult(ContentType contentType, bool expected)
    {
        // Act
        var result = ContentHotswapClassification.IsHotswappable(contentType);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that IsLocked returns the exact opposite of IsHotswappable.
    /// </summary>
    /// <param name="contentType">The content type under test.</param>
    /// <param name="expected">The expected boolean result.</param>
    [Theory]
    [InlineData(ContentType.Map, false)]
    [InlineData(ContentType.MapPack, false)]
    [InlineData(ContentType.Patch, true)]
    [InlineData(ContentType.Replay, false)]
    [InlineData(ContentType.Mod, true)]
    [InlineData(ContentType.GameClient, true)]
    [InlineData(ContentType.GameInstallation, true)]
    [InlineData(ContentType.Addon, true)]
    [InlineData(ContentType.Executable, true)]
    [InlineData(ContentType.ModdingTool, true)]
    [InlineData(ContentType.Mission, true)]
    [InlineData(ContentType.Skin, true)]
    [InlineData(ContentType.LanguagePack, true)]
    [InlineData(ContentType.ContentBundle, true)]
    [InlineData(ContentType.PublisherReferral, true)]
    [InlineData(ContentType.ContentReferral, true)]
    [InlineData(ContentType.Video, true)]
    [InlineData(ContentType.Screensaver, true)]
    [InlineData(ContentType.UnknownContentType, true)]
    public void IsLocked_ReturnsOppositeOfIsHotswappable(ContentType contentType, bool expected)
    {
        // Act
        var result = ContentHotswapClassification.IsLocked(contentType);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that IsHotswappable and IsLocked throw ArgumentNullException when manifest is null.
    /// </summary>
    [Fact]
    public void IsHotswappable_NullManifest_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ContentHotswapClassification.IsHotswappable(null!));
        Assert.Throws<ArgumentNullException>(() => ContentHotswapClassification.IsLocked(null!));
    }

    /// <summary>
    /// Verifies that IsHotswappable with ContentManifest returns false if any file targets Workspace or System.
    /// </summary>
    [Fact]
    public void IsHotswappable_ManifestWithWorkspaceFiles_ReturnsFalse()
    {
        // Arrange
        var manifest = new GenHub.Core.Models.Manifest.ContentManifest
        {
            Id = GenHub.Core.Models.Manifest.ManifestId.Create("1.0.0.mappack.mixed"),
            Name = "Mixed MapPack",
            ContentType = ContentType.MapPack,
            Files =
            [
                new GenHub.Core.Models.Manifest.ManifestFile { RelativePath = "Maps/desert.map", InstallTarget = GenHub.Core.Models.Enums.ContentInstallTarget.UserMapsDirectory },
                new GenHub.Core.Models.Manifest.ManifestFile { RelativePath = "INIData.big", InstallTarget = GenHub.Core.Models.Enums.ContentInstallTarget.Workspace },
            ],
        };

        // Act & Assert
        Assert.False(ContentHotswapClassification.IsHotswappable(manifest));
        Assert.True(ContentHotswapClassification.IsLocked(manifest));
    }

    /// <summary>
    /// Verifies that IsHotswappable with ContentManifest returns true when all files target user data.
    /// </summary>
    [Fact]
    public void IsHotswappable_ManifestWithOnlyUserDataFiles_ReturnsTrue()
    {
        // Arrange
        var manifest = new GenHub.Core.Models.Manifest.ContentManifest
        {
            Id = GenHub.Core.Models.Manifest.ManifestId.Create("1.0.0.map.desert"),
            Name = "Desert Map",
            ContentType = ContentType.Map,
            Files =
            [
                new GenHub.Core.Models.Manifest.ManifestFile { RelativePath = "Maps/desert.map", InstallTarget = GenHub.Core.Models.Enums.ContentInstallTarget.UserMapsDirectory },
            ],
        };

        // Act & Assert
        Assert.True(ContentHotswapClassification.IsHotswappable(manifest));
        Assert.False(ContentHotswapClassification.IsLocked(manifest));
    }

    /// <summary>
    /// Verifies that IsHotswappable with ContentManifest accounts for variants correctly.
    /// </summary>
    [Fact]
    public void IsHotswappable_ManifestWithVariantTargetingWorkspace_ReturnsFalse()
    {
        // Arrange
        var manifest = new GenHub.Core.Models.Manifest.ContentManifest
        {
            Id = GenHub.Core.Models.Manifest.ManifestId.Create("1.0.0.mappack.variant"),
            Name = "Variant MapPack",
            ContentType = ContentType.MapPack,
            Variants =
            [
                new GenHub.Core.Models.Manifest.ArtifactVariant
                {
                    Files =
                    [
                        new GenHub.Core.Models.Manifest.ManifestFile { RelativePath = "INIData.big", InstallTarget = GenHub.Core.Models.Enums.ContentInstallTarget.Workspace },
                    ],
                },
            ],
        };

        // Act & Assert
        Assert.False(ContentHotswapClassification.IsHotswappable(manifest));
        Assert.True(ContentHotswapClassification.IsLocked(manifest));
    }

    /// <summary>
    /// Verifies that IsHotswappable returns true when the manifest has no files that target workspace.
    /// </summary>
    [Fact]
    public void IsHotswappable_ManifestWithDefaultUserDataTarget_ReturnsTrue()
    {
        // Arrange
        var manifest = new GenHub.Core.Models.Manifest.ContentManifest
        {
            Id = GenHub.Core.Models.Manifest.ManifestId.Create("1.0.0.map.custom"),
            Name = "Custom Map",
            ContentType = ContentType.Map,
            Variants =
            [
                new GenHub.Core.Models.Manifest.ArtifactVariant
                {
                    Files =
                    [
                        new GenHub.Core.Models.Manifest.ManifestFile { RelativePath = "Custom.map", InstallTarget = GenHub.Core.Models.Enums.ContentInstallTarget.UserMapsDirectory },
                    ],
                },
            ],
        };

        // Act & Assert
        Assert.True(ContentHotswapClassification.IsHotswappable(manifest));
        Assert.False(ContentHotswapClassification.IsLocked(manifest));
    }

    /// <summary>
    /// Verifies that IsHotswappable returns false when the manifest declares variants but none resolve.
    /// </summary>
    [Fact]
    public void IsHotswappable_ManifestWithUnresolvableVariants_ReturnsFalse()
    {
        // Arrange
        var manifest = new GenHub.Core.Models.Manifest.ContentManifest
        {
            Id = GenHub.Core.Models.Manifest.ManifestId.Create("1.0.0.map.unmatched"),
            Name = "Unmatched Map",
            ContentType = ContentType.Map,
            Variants =
            [
                new GenHub.Core.Models.Manifest.ArtifactVariant
                {
                    RuntimeIdentifiers = ["unsupported-platform-rid-123"],
                    Files =
                    [
                        new GenHub.Core.Models.Manifest.ManifestFile { RelativePath = "Custom.map", InstallTarget = GenHub.Core.Models.Enums.ContentInstallTarget.UserMapsDirectory },
                    ],
                },
            ],
        };

        // Act & Assert
        Assert.False(ContentHotswapClassification.IsHotswappable(manifest));
        Assert.True(ContentHotswapClassification.IsLocked(manifest));
    }
}
