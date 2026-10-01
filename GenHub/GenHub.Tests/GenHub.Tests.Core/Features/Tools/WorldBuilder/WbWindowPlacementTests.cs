// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using Avalonia;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Common;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for <see cref="WbWindowPlacement"/> and <see cref="WbTutorialHints"/>.
/// </summary>
public sealed class WbWindowPlacementTests
{
    /// <summary>
    /// Verifies unusable saved geometry falls back.
    /// </summary>
    [Fact]
    public void ClampToVisible_ZeroSize_ReturnsFallback()
    {
        // Arrange
        var fallback = new PixelRect(10, 10, 800, 600);
        var screens = new PixelRect(0, 0, 1920, 1080);

        // Act
        var clamped = WbWindowPlacement.ClampToVisible(new PixelRect(0, 0, 0, 0), screens, fallback);

        // Assert
        clamped.Should().Be(fallback);
    }

    /// <summary>
    /// Verifies on-screen geometry passes through unchanged.
    /// </summary>
    [Fact]
    public void ClampToVisible_OnScreen_Unchanged()
    {
        // Arrange
        var saved = new PixelRect(100, 100, 800, 600);
        var screens = new PixelRect(0, 0, 1920, 1080);

        // Act
        var clamped = WbWindowPlacement.ClampToVisible(saved, screens, new PixelRect(0, 0, 640, 480));

        // Assert
        clamped.Should().Be(saved);
    }

    /// <summary>
    /// Verifies off-screen geometry is pulled back with a visible strip.
    /// </summary>
    [Fact]
    public void ClampToVisible_OffScreen_PulledIntoView()
    {
        // Arrange
        var screens = new PixelRect(0, 0, 1920, 1080);

        // Act
        var clamped = WbWindowPlacement.ClampToVisible(new PixelRect(-2000, -2000, 800, 600), screens, new PixelRect(0, 0, 640, 480));

        // Assert
        clamped.X.Should().BeGreaterOrEqualTo(screens.X + (int)WbWindowPlacement.MinVisibleStripPixels - 800);
        clamped.Y.Should().BeGreaterOrEqualTo(screens.Y + (int)WbWindowPlacement.MinVisibleStripPixels - 600);
        clamped.X.Should().BeLessThan(screens.Width);
        clamped.Y.Should().BeLessThan(screens.Height);
    }

    /// <summary>
    /// Verifies oversized geometry shrinks to the visible bounds.
    /// </summary>
    [Fact]
    public void ClampToVisible_Oversized_ShrinksToScreen()
    {
        // Arrange
        var screens = new PixelRect(0, 0, 1920, 1080);

        // Act
        var clamped = WbWindowPlacement.ClampToVisible(new PixelRect(0, 0, 4000, 3000), screens, new PixelRect(0, 0, 640, 480));

        // Assert
        clamped.Width.Should().Be(1920);
        clamped.Height.Should().Be(1080);
    }

    /// <summary>
    /// Verifies geometry save, restore, and reset round-trips through settings.
    /// </summary>
    [Fact]
    public void SaveGeometry_RoundTrip_PersistsAndResets()
    {
        // Arrange
        var backing = new UserSettings();
        var settings = CreateSettingsService(backing);
        var geometry = new WbWindowGeometry { X = 10, Y = 20, Width = 640, Height = 480 };

        // Act
        WbWindowPlacement.SaveGeometry(settings, "ScriptEditor", geometry);
        var restored = WbWindowPlacement.GetSavedGeometry(settings, "ScriptEditor");

        // Assert
        restored.Should().BeEquivalentTo(geometry);
        WbWindowPlacement.GetSavedGeometry(settings, "Ghost").Should().BeNull();

        // Act
        WbWindowPlacement.ResetAll(settings);

        // Assert
        WbWindowPlacement.GetSavedGeometry(settings, "ScriptEditor").Should().BeNull();
    }

    /// <summary>
    /// Verifies tutorial hints show until dismissed or disabled.
    /// </summary>
    [Fact]
    public void TutorialHints_ShowDismissDisable_Behaves()
    {
        // Arrange
        var backing = new UserSettings();
        var settings = CreateSettingsService(backing);

        // Act & Assert
        WbTutorialHints.ShouldShowHint(settings, "Fullscreen").Should().BeTrue();
        WbTutorialHints.DismissHint(settings, "Fullscreen");
        WbTutorialHints.ShouldShowHint(settings, "Fullscreen").Should().BeFalse();
        WbTutorialHints.ShouldShowHint(settings, "Other").Should().BeTrue();

        backing.WorldBuilder.ShowTutorialHints = false;
        WbTutorialHints.ShouldShowHint(settings, "Other").Should().BeFalse();
    }

    /// <summary>
    /// Verifies WorldBuilder settings deep-clone through UserSettings.
    /// </summary>
    [Fact]
    public void UserSettingsClone_WorldBuilder_DeepCopies()
    {
        // Arrange
        var settings = new UserSettings();
        settings.WorldBuilder.UndoDepth = 77;
        settings.WorldBuilder.WindowGeometries["Panel"] = new WbWindowGeometry { X = 1, Y = 2, Width = 3, Height = 4 };
        settings.WorldBuilder.DismissedHints.Add("Hint");

        // Act
        var clone = settings.Clone();

        // Assert
        clone.WorldBuilder.Should().BeEquivalentTo(settings.WorldBuilder);
        clone.WorldBuilder.WindowGeometries.Should().NotBeSameAs(settings.WorldBuilder.WindowGeometries);
        clone.WorldBuilder.DismissedHints.Should().NotBeSameAs(settings.WorldBuilder.DismissedHints);
    }

    private static IUserSettingsService CreateSettingsService(UserSettings backing)
    {
        var mock = new Mock<IUserSettingsService>();
        mock.Setup(service => service.Get()).Returns(backing);
        mock.Setup(service => service.Update(It.IsAny<Action<UserSettings>>()))
            .Callback<Action<UserSettings>>(apply => apply(backing));
        return mock.Object;
    }
}
