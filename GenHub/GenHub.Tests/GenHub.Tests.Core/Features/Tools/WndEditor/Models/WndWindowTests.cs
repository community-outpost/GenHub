using FluentAssertions;
using GenHub.Core.Models.Tools.WndEditor;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Models;

/// <summary>
/// Unit tests for <see cref="WndWindow"/> property case-insensitivity and lifecycle flags.
/// </summary>
public sealed class WndWindowTests
{
    /// <summary>
    /// Tests that window properties are case-insensitive when getting, setting, and removing.
    /// </summary>
    [Fact]
    public void Properties_CaseInsensitiveLookupAndModification()
    {
        // Arrange
        var window = new WndWindow();
        window.SetProperty("windowtype", "USER");

        // Act & Assert: GetProperty is case-insensitive
        window.GetProperty("WINDOWTYPE").Should().Be("USER");
        window.GetProperty("WindowType").Should().Be("USER");

        // Set with different casing updates rather than duplicates
        window.SetProperty("WindowType", "BUTTON");
        window.Properties.Should().ContainSingle();
        window.GetProperty("WINDOWTYPE").Should().Be("BUTTON");

        // Remove is case-insensitive
        window.RemoveProperty("WINDOWTYPE");
        window.Properties.Should().BeEmpty();
        window.GetProperty("windowtype").Should().BeNull();
    }

    /// <summary>
    /// Tests that <see cref="WndWindow.HasEndAllChildren"/> defaults to false and can be toggled.
    /// </summary>
    [Fact]
    public void HasEndAllChildren_DefaultsToFalse_CanBeSet()
    {
        var window = new WndWindow();
        window.HasEndAllChildren.Should().BeFalse();

        window.HasEndAllChildren = true;
        window.HasEndAllChildren.Should().BeTrue();
    }
}
