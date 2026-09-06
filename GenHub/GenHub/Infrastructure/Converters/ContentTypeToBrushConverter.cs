using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a ContentType enum value to a SolidColorBrush for UI visual distinction.
/// </summary>
public class ContentTypeToBrushConverter : IValueConverter
{
    /// <summary>
    /// Gets the singleton instance of the converter.
    /// </summary>
    public static readonly ContentTypeToBrushConverter Instance = new();

    /// <summary>
    /// Converts a ContentType to a SolidColorBrush.
    /// </summary>
    /// <param name="value">The ContentType value to convert.</param>
    /// <param name="targetType">The target type (ignored).</param>
    /// <param name="parameter">Optional parameter (ignored).</param>
    /// <param name="culture">The culture (ignored).</param>
    /// <returns>A SolidColorBrush representing the content type color.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ContentType contentType)
        {
            return contentType switch
            {
                ContentType.GameClient => new SolidColorBrush(Color.Parse("#06B6D4")),
                ContentType.Mod => new SolidColorBrush(Color.Parse("#A855F7")),
                ContentType.Patch => new SolidColorBrush(Color.Parse("#F59E0B")),
                ContentType.Map or ContentType.MapPack => new SolidColorBrush(Color.Parse("#10B981")),
                ContentType.Addon => new SolidColorBrush(Color.Parse("#EC4899")),
                ContentType.ModdingTool or ContentType.Executable => new SolidColorBrush(Color.Parse("#38BDF8")),
                ContentType.ContentBundle => new SolidColorBrush(Color.Parse("#6366F1")),
                ContentType.Mission => new SolidColorBrush(Color.Parse("#F97316")),
                ContentType.Skin or ContentType.LanguagePack => new SolidColorBrush(Color.Parse("#8B5CF6")),
                _ => new SolidColorBrush(Color.Parse("#A855F7")),
            };
        }

        return new SolidColorBrush(Color.Parse("#A855F7"));
    }

    /// <summary>
    /// Converts back from a brush to a ContentType (not supported).
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">Optional parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns><see cref="AvaloniaProperty.UnsetValue"/> as two-way conversion is not supported.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
