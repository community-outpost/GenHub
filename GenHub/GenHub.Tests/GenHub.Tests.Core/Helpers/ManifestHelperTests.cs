using System;
using System.Collections.Generic;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
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

        Assert.Same(reference, ManifestHelper.SelectPrimaryManifest(null!, reference));
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
        };

        var m2 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg-2"),
            Name = "Pkg 2",
            ContentType = ContentType.Addon,
        };

        var reference = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.addon.pkg"),
            Name = "Pkg",
            ContentType = ContentType.Addon,
        };

        var result = ManifestHelper.SelectPrimaryManifest([m1, m2], reference);
        Assert.Same(m1, result);
    }
}
