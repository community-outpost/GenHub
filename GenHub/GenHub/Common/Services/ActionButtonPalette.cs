using Avalonia;
using Avalonia.Media;
using GenHub.Core.Constants;
using System;

namespace GenHub.Common.Services;

/// <summary>Opaque primary action fills designed for white labels and icons.</summary>
/// <param name="Rest">The resting fill.</param>
/// <param name="Hover">The pointer-over fill.</param>
/// <param name="Pressed">The pressed fill.</param>
public sealed record ActionButtonPalette(Color Rest, Color Hover, Color Pressed)
{
    /// <summary>Derives button fills without changing the application's navigation accent.</summary>
    /// <param name="accent">The theme accent whose RGB proportions identify the action hue.</param>
    /// <returns>Opaque fills with at least 4.5:1 contrast against white.</returns>
    public static ActionButtonPalette Create(Color accent)
    {
        var defaultAccent = Color.Parse(ThemeConstants.DefaultTheme.PrimaryHex);
        if (accent.R == defaultAccent.R && accent.G == defaultAccent.G && accent.B == defaultAccent.B)
        {
            // Keep the approved purple treatment independent of a theme's identifier.
            return new(
                Color.Parse(ThemeConstants.DefaultActionRestHex),
                Color.Parse(ThemeConstants.DefaultActionHoverHex),
                Color.Parse(ThemeConstants.DefaultActionPressedHex));
        }

        return new(CreateFill(accent, 6.2, 0.82), CreateFill(accent, 5.2, 1.0), CreateFill(accent, 7.5, 0.68));
    }

    /// <summary>Creates shared gradient fills with readable white ink for every interaction state.</summary>
    /// <param name="accent">The navigation accent used to derive the action hue.</param>
    /// <returns>The resting, pointer-over and pressed gradients.</returns>
    public static (IBrush Normal, IBrush Hover, IBrush Pressed) CreateBrushes(Color accent)
    {
        var palette = Create(accent);
        var defaultAccent = Color.Parse(ThemeConstants.DefaultTheme.PrimaryHex);
        if (accent.R == defaultAccent.R && accent.G == defaultAccent.G && accent.B == defaultAccent.B)
        {
            return (
                Gradient(Color.Parse(ThemeConstants.DefaultActionRestGradientStartHex), Color.Parse(ThemeConstants.DefaultActionRestGradientEndHex)),
                Gradient(Color.Parse(ThemeConstants.DefaultActionHoverHex), Color.Parse(ThemeConstants.DefaultActionRestHex)),
                Gradient(Color.Parse(ThemeConstants.DefaultActionPressedGradientStartHex), Color.Parse(ThemeConstants.DefaultActionPressedGradientEndHex)));
        }

        var deeper = Color.FromRgb((byte)(palette.Pressed.R * 0.85), (byte)(palette.Pressed.G * 0.85), (byte)(palette.Pressed.B * 0.85));
        return (Gradient(palette.Rest, palette.Pressed), Gradient(palette.Hover, palette.Rest), Gradient(palette.Pressed, deeper));
    }

    private static IBrush Gradient(Color start, Color end) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(start, 0), new GradientStop(end, 1) },
    };

    private static Color CreateFill(Color accent, double minimumContrast, double maximumScale)
    {
        // Scaling channels together preserves the hue, including custom theme colors.
        // Normalize first so very dark accents can still have distinct interaction states.
        var maximum = Math.Max(accent.R, Math.Max(accent.G, accent.B));
        var red = maximum == 0 ? 1 : (double)accent.R / maximum;
        var green = maximum == 0 ? 1 : (double)accent.G / maximum;
        var blue = maximum == 0 ? 1 : (double)accent.B / maximum;
        var low = 0.0;

        // Cap each state separately: saturated blue already has high white contrast at full intensity.
        var high = maximumScale;
        var result = Colors.Black;
        for (var iteration = 0; iteration < 24; iteration++)
        {
            var scale = (low + high) / 2;
            var candidate = Color.FromRgb(
                (byte)Math.Floor(red * 255 * scale),
                (byte)Math.Floor(green * 255 * scale),
                (byte)Math.Floor(blue * 255 * scale));
            if (WhiteContrast(candidate) >= minimumContrast)
            {
                result = candidate;
                low = scale;
            }
            else
            {
                high = scale;
            }
        }

        return result;
    }

    private static double WhiteContrast(Color fill)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Linear(fill.R)) + (0.7152 * Linear(fill.G)) + (0.0722 * Linear(fill.B));
        return 1.05 / (luminance + 0.05);
    }
}
