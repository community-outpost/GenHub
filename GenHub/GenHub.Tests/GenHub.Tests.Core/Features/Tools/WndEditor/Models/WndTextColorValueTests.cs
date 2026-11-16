using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for <see cref="WndTextColorValue"/>.
/// </summary>
public sealed class WndTextColorValueTests
{
    private const string SampleValue = "ENABLED: 255 255 255 0, ENABLEDBORDER: 255 255 255 0, "
        + "DISABLED: 200 200 200 0, DISABLEDBORDER: 200 200 200 0, "
        + "HILITE: 255 255 0 0, HILITEBORDER: 255 255 0 0";

    /// <summary>
    /// Tests that a valid value parses into components.
    /// </summary>
    [Fact]
    public void TryParse_ValidValue_ParsesComponents()
    {
        // Act
        var parsed = WndTextColorValue.TryParse(SampleValue, out var textColor);

        // Assert
        parsed.Should().BeTrue();
        textColor.Should().Be(new WndTextColorValue(
            new WndRgbaColor(255, 255, 255, 0),
            new WndRgbaColor(255, 255, 255, 0),
            new WndRgbaColor(200, 200, 200, 0),
            new WndRgbaColor(200, 200, 200, 0),
            new WndRgbaColor(255, 255, 0, 0),
            new WndRgbaColor(255, 255, 0, 0)));
    }

    /// <summary>
    /// Tests that invalid values return false.
    /// </summary>
    /// <param name="value">The value under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("ENABLED: 255 255 255 0")]
    [InlineData("ENABLED: 255 255 255 999, ENABLEDBORDER: 255 255 255 0, DISABLED: 255 255 255 0, DISABLEDBORDER: 255 255 255 0, HILITE: 255 255 255 0, HILITEBORDER: 255 255 255 0")]
    public void TryParse_InvalidValue_ReturnsFalse(string? value)
    {
        // Act
        var parsed = WndTextColorValue.TryParse(value, out var textColor);

        // Assert
        parsed.Should().BeFalse();
        textColor.Should().BeNull();
    }

    /// <summary>
    /// Tests that the canonical form round-trips.
    /// </summary>
    [Fact]
    public void ToString_ReturnsCanonicalForm()
    {
        // Arrange
        WndTextColorValue.TryParse(SampleValue, out var textColor);

        // Act
        var text = textColor!.ToString();

        // Assert
        text.Should().Be(SampleValue);
    }
}
