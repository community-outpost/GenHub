using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a ContentType enum value to a translucent SolidColorBrush for badge background tinting.
/// </summary>
public class ContentTypeToBadgeBackgroundConverter : IValueConverter
{
    /// <summary>
    /// Gets the singleton instance of the converter.
    /// </summary>
    public static readonly ContentTypeToBadgeBackgroundConverter Instance = new();

    /// <summary>
    /// Converts a ContentType to a translucent SolidColorBrush.
    /// </summary>
    /// <param name="value">The ContentType value to convert.</param>
    /// <param name="targetType">The target type (ignored).</param>
    /// <param name="parameter">Optional parameter (ignored).</param>
    /// <param name="culture">The culture (ignored).</param>
    /// <returns>A SolidColorBrush representing the content type badge background tint.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ContentType contentType)
        {
            return contentType switch
            {
                ContentType.GameClient => new SolidColorBrush(Color.Parse("#2506B6D4")),
                ContentType.Mod => new SolidColorBrush(Color.Parse("#25A855F7")),
                ContentType.Patch => new SolidColorBrush(Color.Parse("#25F59E0B")),
                ContentType.Map or ContentType.MapPack => new SolidColorBrush(Color.Parse("#2510B981")),
                ContentType.Addon => new SolidColorBrush(Color.Parse("#25EC4899")),
                ContentType.ModdingTool or ContentType.Executable => new SolidColorBrush(Color.Parse("#2538BDF8")),
                ContentType.ContentBundle => new SolidColorBrush(Color.Parse("#256366F1")),
                ContentType.Mission => new SolidColorBrush(Color.Parse("#25F97316")),
                ContentType.Skin or ContentType.LanguagePack => new SolidColorBrush(Color.Parse("#258B5CF6")),
                _ => new SolidColorBrush(Color.Parse("#25A855F7")),
            };
        }

        return new SolidColorBrush(Color.Parse("#25A855F7"));
    }

    /// <summary>
    /// Converts back from a brush to a ContentType (not implemented).
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">Optional parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns>Throws NotImplementedException.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
