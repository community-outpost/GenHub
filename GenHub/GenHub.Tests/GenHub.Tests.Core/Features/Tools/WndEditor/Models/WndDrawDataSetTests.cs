using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;
using System.Linq;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for <see cref="WndDrawDataSet"/> and <see cref="WndDrawDataEntry"/>.
/// </summary>
public sealed class WndDrawDataSetTests
{
    private const string EmptyEntry = "IMAGE: NoImage, COLOR: 255 255 255 0, BORDERCOLOR: 255 255 255 0";

    /// <summary>
    /// Tests that a single-entry block parses.
    /// </summary>
    [Fact]
    public void TryParse_SingleEntry_ParsesSet()
    {
        // Arrange
        var value = "IMAGE: Circle_Small03_Black, COLOR: 0 0 128 255, BORDERCOLOR: 0 0 0 255";

        // Act
        var parsed = WndDrawDataSet.TryParse(value, out var set);

        // Assert
        parsed.Should().BeTrue();
        set!.Entries.Should().HaveCount(1);
        set.Entries[0].Should().Be(new WndDrawDataEntry(
            "Circle_Small03_Black",
            new WndRgbaColor(0, 0, 128, 255),
            new WndRgbaColor(0, 0, 0, 255)));
        set.Entries[0].IsEmpty.Should().BeFalse();
    }

    /// <summary>
    /// Tests that a nine-entry block parses.
    /// </summary>
    [Fact]
    public void TryParse_NineEntries_ParsesSet()
    {
        // Arrange
        var value = string.Join(
            ", ",
            new[]
            {
                "IMAGE: Circle_Small03_Black, COLOR: 0 0 128 255, BORDERCOLOR: 0 0 0 255",
            }.Concat(Enumerable.Repeat(EmptyEntry, 8)));

        // Act
        var parsed = WndDrawDataSet.TryParse(value, out var set);

        // Assert
        parsed.Should().BeTrue();
        set!.Entries.Should().HaveCount(9);
        set.Entries[0].Should().Be(new WndDrawDataEntry(
            "Circle_Small03_Black",
            new WndRgbaColor(0, 0, 128, 255),
            new WndRgbaColor(0, 0, 0, 255)));
        set.Entries[0].IsEmpty.Should().BeFalse();
        set.Entries[1].IsEmpty.Should().BeTrue();
    }

    /// <summary>
    /// Tests that invalid values return false.
    /// </summary>
    /// <param name="value">The value under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("IMAGE: NoImage, COLOR: 255 255 255 0")]
    public void TryParse_InvalidValue_ReturnsFalse(string? value)
    {
        // Act
        var parsed = WndDrawDataSet.TryParse(value, out var set);

        // Assert
        parsed.Should().BeFalse();
        set.Should().BeNull();
    }

    /// <summary>
    /// Tests that the canonical form round-trips.
    /// </summary>
    [Fact]
    public void ToString_ReturnsCanonicalForm()
    {
        // Arrange
        var value = string.Join(", ", Enumerable.Repeat(EmptyEntry, 9));
        WndDrawDataSet.TryParse(value, out var set);

        // Act
        var text = set!.ToString();

        // Assert
        text.Should().Be(value);
    }

    /// <summary>
    /// Tests that the empty set carries a single empty entry.
    /// </summary>
    [Fact]
    public void Empty_ContainsSingleEmptyEntry()
    {
        // Act
        var set = WndDrawDataSet.Empty;

        // Assert
        set.Entries.Should().HaveCount(1);
        set.Entries.Should().OnlyContain(entry => entry.IsEmpty);
    }
}
