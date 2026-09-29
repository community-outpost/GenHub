using GenHub.Core.Constants;
using GenHub.Core.Models.Providers;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Models.Providers;

/// <summary>
/// Unit tests for <see cref="CatalogContentItem"/> featured presentation.
/// </summary>
public class CatalogContentItemTests
{
    /// <summary>
    /// Verifies non-featured items expose no featured color.
    /// </summary>
    [Fact]
    public void FeaturedColor_NotFeatured_ReturnsNull()
    {
        var item = new CatalogContentItem
        {
            Metadata = new ContentRichMetadata { AccentColor = "#76F525" },
        };

        Assert.Null(item.FeaturedColor);
        Assert.False(item.HasFeaturedColor);
    }

    /// <summary>
    /// Verifies featured items with an accent expose that accent.
    /// </summary>
    [Fact]
    public void FeaturedColor_FeaturedWithAccent_ReturnsAccent()
    {
        var item = new CatalogContentItem
        {
            IsFeatured = true,
            Metadata = new ContentRichMetadata { AccentColor = "#76F525" },
        };

        Assert.Equal("#76F525", item.FeaturedColor);
        Assert.True(item.HasFeaturedColor);
    }

    /// <summary>
    /// Verifies featured items without an accent fall back to default gold.
    /// </summary>
    [Fact]
    public void FeaturedColor_FeaturedWithoutAccent_ReturnsDefaultGold()
    {
        var item = new CatalogContentItem { IsFeatured = true };

        Assert.Equal(CatalogConstants.FeaturedDefaultColor, item.FeaturedColor);
        Assert.True(item.HasFeaturedColor);
    }

    /// <summary>
    /// Verifies toggling the featured flag notifies featured color bindings.
    /// </summary>
    [Fact]
    public void IsFeatured_Set_RaisesFeaturedColorNotifications()
    {
        var item = new CatalogContentItem();
        var notified = new List<string?>();
        item.PropertyChanged += (_, args) => notified.Add(args.PropertyName);

        item.IsFeatured = true;

        Assert.Contains(nameof(CatalogContentItem.FeaturedColor), notified);
        Assert.Contains(nameof(CatalogContentItem.HasFeaturedColor), notified);
    }

    /// <summary>
    /// Verifies replacing metadata notifies featured color bindings.
    /// </summary>
    [Fact]
    public void Metadata_Set_RaisesFeaturedColorNotifications()
    {
        var item = new CatalogContentItem { IsFeatured = true };
        var notified = new List<string?>();
        item.PropertyChanged += (_, args) => notified.Add(args.PropertyName);

        item.Metadata = new ContentRichMetadata { AccentColor = "#0F6A0D" };

        Assert.Contains(nameof(CatalogContentItem.FeaturedColor), notified);
        Assert.Equal("#0F6A0D", item.FeaturedColor);
    }
}
