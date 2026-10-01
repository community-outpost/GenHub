// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using Avalonia;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Tools.WorldBuilder;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Tools.WorldBuilder.Common;

/// <summary>
/// Persists WorldBuilder panel and dialog frame geometry. Ports the QT-02
/// semantics (track position and size per window name, restore on first show,
/// clamp stale off-screen positions back into view, reset all) onto
/// <see cref="IUserSettingsService"/> instead of WorldBuilder.ini.
/// </summary>
public static class WbWindowPlacement
{
    /// <summary>
    /// Minimum visible strip in pixels kept on screen when clamping.
    /// </summary>
    public const double MinVisibleStripPixels = 32.0;

    /// <summary>
    /// Clamps a saved frame rectangle into the visible bounds, keeping at least
    /// a strip visible. Falls back to the supplied default when the saved
    /// geometry is unusable (zero or negative size).
    /// </summary>
    /// <param name="saved">The saved geometry.</param>
    /// <param name="visibleBounds">The visible screen bounds.</param>
    /// <param name="fallback">The fallback geometry.</param>
    /// <returns>The geometry to apply.</returns>
    public static PixelRect ClampToVisible(PixelRect saved, PixelRect visibleBounds, PixelRect fallback)
    {
        if (saved.Width <= 0 || saved.Height <= 0)
        {
            return fallback;
        }

        var width = Math.Min(saved.Width, visibleBounds.Width);
        var height = Math.Min(saved.Height, visibleBounds.Height);
        var strip = (int)Math.Min(MinVisibleStripPixels, Math.Min(width, height));
        var minX = visibleBounds.X + strip - width;
        var maxX = visibleBounds.X + visibleBounds.Width - strip;
        var minY = visibleBounds.Y + strip - height;
        var maxY = visibleBounds.Y + visibleBounds.Height - strip;
        var x = Math.Clamp(saved.X, minX, maxX);
        var y = Math.Clamp(saved.Y, minY, maxY);
        return new PixelRect(x, y, width, height);
    }

    /// <summary>
    /// Restores the saved geometry for a window when one exists.
    /// </summary>
    /// <param name="settingsService">The user settings service.</param>
    /// <param name="windowName">The tracked window name.</param>
    /// <returns>The saved geometry, or null.</returns>
    public static WbWindowGeometry? GetSavedGeometry(IUserSettingsService settingsService, string windowName)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowName);
        return settingsService.Get().WorldBuilder.WindowGeometries.TryGetValue(windowName, out var geometry)
            ? geometry
            : null;
    }

    /// <summary>
    /// Saves the frame geometry for a window. Callers persist via
    /// <see cref="IUserSettingsService.SaveAsync"/> on hide using their own
    /// debounce; positions are clamped back into view on restore.
    /// </summary>
    /// <param name="settingsService">The user settings service.</param>
    /// <param name="windowName">The tracked window name.</param>
    /// <param name="geometry">The frame geometry.</param>
    public static void SaveGeometry(IUserSettingsService settingsService, string windowName, WbWindowGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowName);
        ArgumentNullException.ThrowIfNull(geometry);
        settingsService.Update(settings => settings.WorldBuilder.WindowGeometries[windowName] = geometry);
    }

    /// <summary>
    /// Clears all saved WorldBuilder window geometries.
    /// </summary>
    /// <param name="settingsService">The user settings service.</param>
    public static void ResetAll(IUserSettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(settingsService);
        settingsService.Update(settings => settings.WorldBuilder.WindowGeometries.Clear());
    }

    /// <summary>
    /// Gets the union of the visible bounds across screens for clamping.
    /// </summary>
    /// <param name="screens">The per-screen visible bounds.</param>
    /// <param name="fallback">The bounds when no screen is reported.</param>
    /// <returns>The union bounds.</returns>
    public static PixelRect UnionVisibleBounds(IReadOnlyList<PixelRect> screens, PixelRect fallback)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (screens.Count == 0)
        {
            return fallback;
        }

        return screens.Aggregate(static (left, right) => left.Union(right));
    }
}
