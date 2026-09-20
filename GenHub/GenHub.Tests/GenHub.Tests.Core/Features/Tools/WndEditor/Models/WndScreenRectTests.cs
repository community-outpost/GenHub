using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for <see cref="WndScreenRect"/>.
/// </summary>
public sealed class WndScreenRectTests
{
    /// <summary>
    /// Tests that a valid value parses into components.
    /// </summary>
    [Fact]
    public void TryParse_ValidValue_ParsesComponents()
    {
        // Act
        var parsed = WndScreenRect.TryParse(
            "UPPERLEFT: 0 0, BOTTOMRIGHT: 799 600, CREATIONRESOLUTION: 800 600",
            out var rect);

        // Assert
        parsed.Should().BeTrue();
        rect.Should().Be(new WndScreenRect(0, 0, 799, 600, 800, 600));
        rect!.Width.Should().Be(799);
        rect.Height.Should().Be(600);
    }

    /// <summary>
    /// Tests that a multi-line value parses into components.
    /// </summary>
    [Fact]
    public void TryParse_MultiLineValue_ParsesComponents()
    {
        // Act
        var parsed = WndScreenRect.TryParse(
            "UPPERLEFT: 10 20,\nBOTTOMRIGHT: 110 60,\nCREATIONRESOLUTION: 800 600",
            out var rect);

        // Assert
        parsed.Should().BeTrue();
        rect.Should().Be(new WndScreenRect(10, 20, 110, 60, 800, 600));
    }

    /// <summary>
    /// Tests that invalid values return false.
    /// </summary>
    /// <param name="value">The value under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("UPPERLEFT: 0 0, BOTTOMRIGHT: 799 600")]
    [InlineData("UPPERLEFT: 0, BOTTOMRIGHT: 799 600, CREATIONRESOLUTION: 800 600")]
    [InlineData("UPPERLEFT: a b, BOTTOMRIGHT: 799 600, CREATIONRESOLUTION: 800 600")]
    public void TryParse_InvalidValue_ReturnsFalse(string? value)
    {
        // Act
        var parsed = WndScreenRect.TryParse(value, out var rect);

        // Assert
        parsed.Should().BeFalse();
        rect.Should().BeNull();
    }

    /// <summary>
    /// Tests that the canonical form round-trips.
    /// </summary>
    [Fact]
    public void ToString_ReturnsCanonicalForm()
    {
        // Arrange
        var rect = new WndScreenRect(0, 0, 799, 600, 800, 600);

        // Act
        var text = rect.ToString();

        // Assert
        text.Should().Be("UPPERLEFT: 0 0, BOTTOMRIGHT: 799 600, CREATIONRESOLUTION: 800 600");
    }
}
