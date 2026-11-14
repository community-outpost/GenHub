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
            return new(Color.Parse("#7540B8"), Color.Parse("#8651C9"), Color.Parse("#65339F"));
        }

        return new(CreateFill(accent, 6.2), CreateFill(accent, 5.2), CreateFill(accent, 7.5));
    }

    private static Color CreateFill(Color accent, double minimumContrast)
    {
        // Scaling channels together preserves the hue, including custom theme colors.
        // Normalize first so very dark accents can still have distinct interaction states.
        var maximum = Math.Max(accent.R, Math.Max(accent.G, accent.B));
        var red = maximum == 0 ? 1 : (double)accent.R / maximum;
        var green = maximum == 0 ? 1 : (double)accent.G / maximum;
        var blue = maximum == 0 ? 1 : (double)accent.B / maximum;
        var low = 0.0;
        var high = 1.0;
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
