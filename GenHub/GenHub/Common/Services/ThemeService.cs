using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Messages;
using GenHub.Core.Models.Theming;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Service that manages dynamic application accent color themes at runtime.
/// </summary>
public class ThemeService(
    IUserSettingsService userSettingsService,
    ILogger<ThemeService> logger) : IThemeService
{
    /// <inheritdoc/>
    public IReadOnlyList<ColorTheme> AvailableThemes => ThemeConstants.AllThemes;

    /// <inheritdoc/>
    public ColorTheme CurrentTheme { get; private set; } = ThemeConstants.DefaultTheme;

    /// <inheritdoc/>
    public void InitializeTheme()
    {
        var settingsTheme = userSettingsService.Get().Theme;
        if (!string.IsNullOrWhiteSpace(settingsTheme))
        {
            ApplyTheme(settingsTheme);
        }
        else
        {
            ApplyTheme(ThemeConstants.DefaultTheme);
        }
    }

    /// <inheritdoc/>
    public void ApplyTheme(string themeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(themeId);

        var theme = AvailableThemes.FirstOrDefault(t =>
            string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.DisplayName, themeId, StringComparison.OrdinalIgnoreCase))
            ?? ThemeConstants.DefaultTheme;

        ApplyTheme(theme);
    }

    /// <inheritdoc/>
    public void ApplyTheme(ColorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        CurrentTheme = theme;

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyThemeToResources(theme);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ApplyThemeToResources(theme));
        }
    }

    private void ApplyThemeToResources(ColorTheme theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        try
        {
            var primaryColor = Color.Parse(theme.PrimaryHex);
            var lightColor = Color.Parse(theme.LightHex);
            var darkColor = Color.Parse(theme.DarkHex);
            var glowColor = Color.Parse(theme.GlowHex);
            var badgeBgColor = Color.FromArgb(0x20, primaryColor.R, primaryColor.G, primaryColor.B);
            var badgeFgColor = Color.FromArgb(0xCC, primaryColor.R, primaryColor.G, primaryColor.B);
            var sidebarSelectBgColor = Color.FromArgb(0x4D, darkColor.R, darkColor.G, darkColor.B);

            var resources = Application.Current.Resources;

            // Update Colors
            resources[ThemeResourceKeys.AccentColor] = primaryColor;
            resources[ThemeResourceKeys.SystemAccentColor] = primaryColor;
            resources[ThemeResourceKeys.PrimaryButtonBackgroundDark] = primaryColor;
            resources[ThemeResourceKeys.AccentBadgeBackgroundColor] = badgeBgColor;
            resources[ThemeResourceKeys.AccentBadgeForegroundColor] = badgeFgColor;
            resources[ThemeResourceKeys.AccentGlowColor] = glowColor;
            resources[ThemeResourceKeys.PrimaryGradientStart] = lightColor;
            resources[ThemeResourceKeys.PrimaryGradientEnd] = darkColor;

            // Update Brushes
            resources[ThemeResourceKeys.AccentBrush] = new SolidColorBrush(primaryColor);
            resources[ThemeResourceKeys.AccentColorBrush] = new SolidColorBrush(primaryColor);
            resources[ThemeResourceKeys.AccentGlowBrush] = new SolidColorBrush(glowColor);
            resources[ThemeResourceKeys.SystemAccentColorBrush] = new SolidColorBrush(primaryColor);
            resources[ThemeResourceKeys.PrimaryButtonBackground] = new SolidColorBrush(primaryColor);
            resources[ThemeResourceKeys.SidebarSelectedIndicator] = new SolidColorBrush(primaryColor);
            resources[ThemeResourceKeys.ScrollbarThumbPressedBrush] = new SolidColorBrush(primaryColor);
            resources[ThemeResourceKeys.ScrollBarThumbFillPressed] = new SolidColorBrush(primaryColor);
            resources[ThemeResourceKeys.AccentBadgeBackgroundBrush] = new SolidColorBrush(badgeBgColor);
            resources[ThemeResourceKeys.AccentBadgeForegroundBrush] = new SolidColorBrush(badgeFgColor);
            resources[ThemeResourceKeys.SidebarItemSelectedBackground] = new SolidColorBrush(sidebarSelectBgColor);
            resources[ThemeResourceKeys.SidebarItemSelectedBorder] = new SolidColorBrush(primaryColor);
            resources[ThemeResourceKeys.ComboBoxItemBackgroundSelected] = new SolidColorBrush(badgeBgColor);
            resources[ThemeResourceKeys.ComboBoxItemBackgroundSelectedPointerOver] = new SolidColorBrush(primaryColor);

            // Update Linear Gradient Brush
            var gradientBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(lightColor, 0),
                    new GradientStop(darkColor, 1),
                },
            };
            resources[ThemeResourceKeys.PrimaryGradientBrush] = gradientBrush;

            WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(theme.Id));
            logger.LogDebug("Applied color theme '{ThemeName}' ({ThemeId})", theme.DisplayName, theme.Id);
        }
        catch (FormatException ex)
        {
            logger.LogError(ex, "Failed to parse color hex for theme {ThemeId}", theme.Id);
        }
    }
}
