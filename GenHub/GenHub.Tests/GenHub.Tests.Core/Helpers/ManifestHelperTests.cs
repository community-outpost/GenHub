using GenHub.Core.Helpers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System;
using System.Collections.Generic;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="ManifestHelper"/>.
/// </summary>
public sealed class ManifestHelperTests
{
    /// <summary>
    /// Tests that SelectPrimaryManifest returns the reference manifest when manifests list is null or empty.
    /// </summary>
    [Fact]
    public void SelectPrimaryManifest_WithEmptyOrNullManifests_ReturnsReferenceManifest()
    {
        var reference = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg"),
            Name = "Pkg",
            ContentType = ContentType.Addon,
        };

        Assert.Same(reference, ManifestHelper.SelectPrimaryManifest(null, reference));
        Assert.Same(reference, ManifestHelper.SelectPrimaryManifest([], reference));
    }

    /// <summary>
    /// Tests that SelectPrimaryManifest returns the manifest matching the requested SelectedVariantId.
    /// </summary>
    [Fact]
    public void SelectPrimaryManifest_WithMatchingSelectedVariantId_ReturnsMatchingManifest()
    {
        var m1 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-en"),
            Name = "Pkg (EN)",
            ContentType = ContentType.Addon,
            Metadata = new ContentMetadata { SelectedVariantId = "en" },
        };

        var m2 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-ru"),
            Name = "Pkg (RU)",
            ContentType = ContentType.Addon,
            Metadata = new ContentMetadata { SelectedVariantId = "ru" },
        };

        var reference = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg"),
            Name = "Pkg",
            ContentType = ContentType.Addon,
            Metadata = new ContentMetadata { SelectedVariantId = "ru" },
        };

        var result = ManifestHelper.SelectPrimaryManifest([m1, m2], reference);
        Assert.Same(m2, result);
    }

    /// <summary>
    /// Tests that SelectPrimaryManifest prioritizes an exact SelectedVariantId match over an ID-suffix match.
    /// </summary>
    [Fact]
    public void SelectPrimaryManifest_PrefersExactSelectedVariantIdOverIdSuffix()
    {
        var suffixMatch = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-ru"),
            Name = "Suffix Match",
            ContentType = ContentType.Addon,
            Metadata = new ContentMetadata { SelectedVariantId = "other" },
        };

        var exactMatch = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-custom"),
            Name = "Exact Match",
            ContentType = ContentType.Addon,
            Metadata = new ContentMetadata { SelectedVariantId = "ru" },
        };

        var reference = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg"),
            Name = "Pkg",
            ContentType = ContentType.Addon,
            Metadata = new ContentMetadata { SelectedVariantId = "ru" },
        };

        // Put suffixMatch first in candidates; exactMatch should still be selected
        var result = ManifestHelper.SelectPrimaryManifest([suffixMatch, exactMatch], reference);
        Assert.Same(exactMatch, result);
    }

    /// <summary>
    /// Tests that SelectPrimaryManifest returns the manifest matching variant tags.
    /// </summary>
    [Fact]
    public void SelectPrimaryManifest_WithMatchingVariantInTags_ReturnsMatchingManifest()
    {
        var m1 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-en"),
            Name = "Pkg (EN)",
            ContentType = ContentType.Addon,
            Metadata = new ContentMetadata { Tags = ["variant:en"] },
        };

        var m2 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-ru"),
            Name = "Pkg (RU)",
            ContentType = ContentType.Addon,
            Metadata = new ContentMetadata { Tags = ["variant:ru"] },
        };

        var reference = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg"),
            Name = "Pkg",
            ContentType = ContentType.Addon,
            Metadata = new ContentMetadata { Tags = ["selectedVariant:ru"] },
        };

        var result = ManifestHelper.SelectPrimaryManifest([m1, m2], reference);
        Assert.Same(m2, result);
    }

    /// <summary>
    /// Tests that SelectPrimaryManifest falls back to matching TargetGame when no variant is specified.
    /// </summary>
    [Fact]
    public void SelectPrimaryManifest_WithTargetGameMatch_ReturnsMatchingManifest()
    {
        var m1 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-ccg"),
            Name = "Pkg (CCG)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.Generals,
        };

        var m2 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-zh"),
            Name = "Pkg (ZH)",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        var reference = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg"),
            Name = "Pkg",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        var result = ManifestHelper.SelectPrimaryManifest([m1, m2], reference);
        Assert.Same(m2, result);
    }

    /// <summary>
    /// Tests that SelectPrimaryManifest falls back to the first manifest when no variant or game match exists.
    /// </summary>
    [Fact]
    public void SelectPrimaryManifest_WithoutMatch_FallsBackToFirstManifest()
    {
        var m1 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-1"),
            Name = "Pkg 1",
            ContentType = ContentType.Addon,
            TargetGame = GameType.Generals,
        };

        var m2 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-2"),
            Name = "Pkg 2",
            ContentType = ContentType.Addon,
            TargetGame = GameType.Generals,
        };

        var reference = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg"),
            Name = "Pkg",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        var result = ManifestHelper.SelectPrimaryManifest([m1, m2], reference);
        Assert.Same(m1, result);
    }

    /// <summary>
    /// Tests that launcher-managed manifests (installations and installation-detected
    /// game clients) are distinguished from publisher downloads.
    /// </summary>
    /// <param name="id">The manifest id under test.</param>
    /// <param name="publisherType">The publisher type to assign, or null to keep the default.</param>
    /// <param name="contentType">The content type to assign.</param>
    /// <param name="expected">The expected classification result.</param>
    [Theory]
    [InlineData("1.104.steam.gameinstallation.generals", "steam", ContentType.GameInstallation, true)]
    [InlineData("1.108.steam.gameclient.zerohour", "steam", ContentType.GameClient, true)]
    [InlineData("1.108.wine.gameclient.zerohour", "retail", ContentType.GameClient, true)]
    [InlineData("1.108.lutris.gameclient.zerohour", "unknown", ContentType.GameClient, true)]
    [InlineData("1.108.custom.gameclient.zerohour", "retail", ContentType.GameClient, true)]
    [InlineData("1.108.custom.gameclient.zerohour", "unknown", ContentType.GameClient, false)]
    [InlineData("1.108.custom.gameclient.zerohour", null, ContentType.GameClient, false)]
    [InlineData("1.108.generalsonline.gameclient.zerohour", "generalsonline", ContentType.GameClient, false)]
    [InlineData("1.20260101.steamworkshop.mod.maps", "steamworkshop", ContentType.Mod, false)]
    [InlineData("1.20260101.generalsonline.patch.hotfix", "generalsonline", ContentType.Patch, false)]
    public void IsLauncherManagedManifest_ClassifiesManifestsCorrectly(
        string id,
        string? publisherType,
        ContentType contentType,
        bool expected)
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = "Test",
            ContentType = contentType,
            TargetGame = GameType.ZeroHour,
        };
        if (publisherType != null)
        {
            manifest.Publisher = new PublisherInfo { PublisherType = publisherType };
        }

        Assert.Equal(expected, ManifestHelper.IsLauncherManagedManifest(manifest));
    }

    /// <summary>
    /// Tests that a null manifest is never launcher-managed.
    /// </summary>
    [Fact]
    public void IsLauncherManagedManifest_WithNull_ReturnsFalse()
    {
        Assert.False(ManifestHelper.IsLauncherManagedManifest(null));
    }
}
