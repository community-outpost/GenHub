using GenHub.Core.Models.Providers;
using GenHub.Features.Content.Services;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Tests mapping catalog tab definitions to runtime tabs in <see cref="CatalogTabMapper"/>.
/// </summary>
public sealed class CatalogTabMapperTests
{
    /// <summary>
    /// Card images outside the allowed schemes are dropped, so an unvalidated tab never renders local files.
    /// </summary>
    /// <param name="imageUrl">The card image URL.</param>
    /// <param name="expected">The image URL expected on the runtime card.</param>
    [Theory]
    [InlineData("https://example.com/card.png", "https://example.com/card.png")]
    [InlineData("avares://GenHub/Assets/Covers/usa-cover.jpg", "avares://GenHub/Assets/Covers/usa-cover.jpg")]
    [InlineData("file:///etc/passwd", null)]
    [InlineData("/Users/someone/secret.png", null)]
    [InlineData("relative/card.png", null)]
    [InlineData("https://drive.google.com/file/d/18onwpszLRsc62P92f7biat6ORbXtL7u4/view?usp=sharing", "https://drive.usercontent.google.com/download?id=18onwpszLRsc62P92f7biat6ORbXtL7u4&export=view")]
    [InlineData("http://example.com/card.png", null)]
    public void Map_CardImageUrl_KeepsOnlyAllowedSchemes(string imageUrl, string? expected)
    {
        var tab = CatalogTabMapper.Map(new CatalogTabDefinition
        {
            TabId = "gallery",
            Header = "Gallery",
            Cards = [new CatalogTabCardDefinition { Title = "Card", ImageUrl = imageUrl }],
        });

        Assert.Equal(expected, Assert.Single(tab.Cards).ImageUrl);
    }

    /// <summary>
    /// The runtime tab gets its own metadata dictionary, so changing it never touches the cached catalog.
    /// </summary>
    [Fact]
    public void Map_Metadata_IsCopied()
    {
        var source = new CatalogTabDefinition
        {
            TabId = "notes",
            Header = "Notes",
            Metadata = new Dictionary<string, object> { ["key"] = "value" },
        };

        var tab = CatalogTabMapper.Map(source);
        tab.Metadata["key"] = "changed";

        Assert.NotSame(source.Metadata, tab.Metadata);
        Assert.Equal("value", source.Metadata["key"]);
    }
}
