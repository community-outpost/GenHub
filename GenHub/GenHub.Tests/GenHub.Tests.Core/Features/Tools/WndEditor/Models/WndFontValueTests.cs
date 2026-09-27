using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for <see cref="WndFontValue"/>.
/// </summary>
public sealed class WndFontValueTests
{
    /// <summary>
    /// Tests that a valid value parses into components.
    /// </summary>
    [Fact]
    public void TryParse_ValidValue_ParsesComponents()
    {
        // Act
        var parsed = WndFontValue.TryParse(
            "NAME: \"Times New Roman\", SIZE: 14, BOLD: 0",
            out var font);

        // Assert
        parsed.Should().BeTrue();
        font.Should().Be(new WndFontValue("Times New Roman", 14, false));
    }

    /// <summary>
    /// Tests that a bold font parses.
    /// </summary>
    [Fact]
    public void TryParse_BoldValue_ParsesBold()
    {
        // Act
        var parsed = WndFontValue.TryParse("NAME: \"Arial\", SIZE: 12, BOLD: 1", out var font);

        // Assert
        parsed.Should().BeTrue();
        font.Should().Be(new WndFontValue("Arial", 12, true));
    }

    /// <summary>
    /// Tests that invalid values return false.
    /// </summary>
    /// <param name="value">The value under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("NAME: \"Arial\", SIZE: 12")]
    [InlineData("NAME: \"Arial\", SIZE: big, BOLD: 0")]
    public void TryParse_InvalidValue_ReturnsFalse(string? value)
    {
        // Act
        var parsed = WndFontValue.TryParse(value, out var font);

        // Assert
        parsed.Should().BeFalse();
        font.Should().BeNull();
    }

    /// <summary>
    /// Tests that the canonical form round-trips.
    /// </summary>
    [Fact]
    public void ToString_ReturnsCanonicalForm()
    {
        // Arrange
        var font = new WndFontValue("Times New Roman", 14, false);

        // Act
        var text = font.ToString();

        // Assert
        text.Should().Be("NAME: \"Times New Roman\", SIZE: 14, BOLD: 0");
    }
}
