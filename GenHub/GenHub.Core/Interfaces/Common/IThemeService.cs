using System.Collections.Generic;
using GenHub.Core.Models.Theming;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Service responsible for managing and applying application color themes at runtime.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Gets all available built-in color themes.
    /// </summary>
    IReadOnlyList<ColorTheme> AvailableThemes { get; }

    /// <summary>
    /// Gets the currently active color theme.
    /// </summary>
    ColorTheme CurrentTheme { get; }

    /// <summary>
    /// Applies the specified color theme by its unique identifier or display name.
    /// </summary>
    /// <param name="themeId">The ID or display name of the theme to apply.</param>
    void ApplyTheme(string themeId);

    /// <summary>
    /// Applies the specified color theme.
    /// </summary>
    /// <param name="theme">The theme to apply.</param>
    void ApplyTheme(ColorTheme theme);

    /// <summary>
    /// Initializes and restores the theme saved in user settings.
    /// </summary>
    void InitializeTheme();
}
