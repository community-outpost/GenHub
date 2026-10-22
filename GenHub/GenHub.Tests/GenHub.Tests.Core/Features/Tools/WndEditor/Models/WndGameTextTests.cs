using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for <see cref="WndGameText"/>.
/// </summary>
public sealed class WndGameTextTests
{
    /// <summary>
    /// Tests that hotkey markers are stripped the way the engine draws them.
    /// </summary>
    /// <param name="value">The localized value.</param>
    /// <param name="expected">The expected drawn text.</param>
    [Theory]
    [InlineData("&Accept", "Accept")]
    [InlineData("Save && Continue", "Save & Continue")]
    [InlineData("No markers", "No markers")]
    [InlineData("GUI:Raw", "GUI:Raw")]
    public void StripHotkeyMarkers_RemovesMarkers(string value, string expected)
    {
        // Act
        var stripped = WndGameText.StripHotkeyMarkers(value);

        // Assert
        stripped.Should().Be(expected);
    }
}
