using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for <see cref="WndMappedImage"/>.
/// </summary>
public sealed class WndMappedImageTests
{
    /// <summary>
    /// Tests that a valid block parses into a mapped image.
    /// </summary>
    [Fact]
    public void ParseDefinitions_ValidBlock_ParsesImage()
    {
        // Arrange
        const string content =
            "MappedImage MenuButton\n" +
            "  Texture = MenuPage\n" +
            "  TextureWidth = 512\n" +
            "  TextureHeight = 512\n" +
            "  Coords = Left:10 Top:20 Right:110 Bottom:60\n" +
            "  Status = NONE\n" +
            "End\n";

        // Act
        var images = WndMappedImage.ParseDefinitions(content);

        // Assert
        images.Should().ContainSingle();
        images[0].Should().Be(new WndMappedImage("MenuButton", "MenuPage", 10, 20, 110, 60, false) { TextureWidth = 512, TextureHeight = 512 });
        images[0].Width.Should().Be(100);
        images[0].Height.Should().Be(40);
    }

    /// <summary>
    /// Tests that the rotated status flag parses.
    /// </summary>
    [Fact]
    public void ParseDefinitions_RotatedStatus_ParsesRotated()
    {
        // Arrange
        const string content =
            "MappedImage PackedIcon\n" +
            "  Texture = IconPage\n" +
            "  Coords = Left:0 Top:0 Right:32 Bottom:32\n" +
            "  Status = ROTATED_90_CLOCKWISE\n" +
            "End\n";

        // Act
        var images = WndMappedImage.ParseDefinitions(content);

        // Assert
        images.Should().ContainSingle();
        images[0].IsRotated.Should().BeTrue();
    }

    /// <summary>
    /// Tests that comments, casing, and quoting are tolerated.
    /// </summary>
    [Fact]
    public void ParseDefinitions_CommentsAndCasing_ParsesImage()
    {
        // Arrange
        const string content =
            "; leading comment\n" +
            "mappedimage LowerButton ; trailing comment\n" +
            "  TEXTURE = \"QuotedPage\" ; quoted value\n" +
            "  coords = LEFT:1 TOP:2 RIGHT:3 BOTTOM:4\n" +
            "END\n";

        // Act
        var images = WndMappedImage.ParseDefinitions(content);

        // Assert
        images.Should().ContainSingle();
        images[0].Should().Be(new WndMappedImage("LowerButton", "QuotedPage", 1, 2, 3, 4, false));
    }

    /// <summary>
    /// Tests that coords with spaces after colons and commas are parsed.
    /// </summary>
    [Fact]
    public void ParseDefinitions_FlexibleCoordsFormatting_ParsesCorrectly()
    {
        // Arrange
        const string content =
            "MappedImage SpacedCoords\n" +
            "  Texture = SpacedPage\n" +
            "  Coords = Left: 15, Top: 25, Right: 115, Bottom: 65\n" +
            "End\n";

        // Act
        var images = WndMappedImage.ParseDefinitions(content);

        // Assert
        images.Should().ContainSingle();
        images[0].Should().Be(new WndMappedImage("SpacedCoords", "SpacedPage", 15, 25, 115, 65, false));
    }

    /// <summary>
    /// Tests that malformed blocks are skipped while valid ones parse.
    /// </summary>
    [Fact]
    public void ParseDefinitions_MalformedBlocks_SkipsThem()
    {
        // Arrange
        const string content =
            "MappedImage MissingTexture\n" +
            "  Coords = Left:0 Top:0 Right:8 Bottom:8\n" +
            "End\n" +
            "MappedImage BadCoords\n" +
            "  Texture = Page\n" +
            "  Coords = Left:8 Top:8 Right:0 Bottom:0\n" +
            "End\n" +
            "MappedImage Unterminated\n" +
            "  Texture = Page\n" +
            "MappedImage Good\n" +
            "  Texture = Page\n" +
            "  Coords = Left:0 Top:0 Right:8 Bottom:8\n" +
            "End\n";

        // Act
        var images = WndMappedImage.ParseDefinitions(content);

        // Assert
        images.Should().ContainSingle();
        images[0].Name.Should().Be("Good");
    }
}
