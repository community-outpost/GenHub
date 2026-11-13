using System;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Game text helpers matching engine label rendering.
/// </summary>
public static class WndGameText
{
    /// <summary>
    /// Strips hotkey markers from a localized value: single ampersands mark the
    /// hotkey character and are not drawn, while doubled ampersands draw literally.
    /// </summary>
    /// <param name="value">The localized value.</param>
    /// <returns>The value as drawn by the engine.</returns>
    public static string StripHotkeyMarkers(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Replace("&&", "\0", StringComparison.Ordinal)
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .Replace("\0", "&", StringComparison.Ordinal);
    }
}
