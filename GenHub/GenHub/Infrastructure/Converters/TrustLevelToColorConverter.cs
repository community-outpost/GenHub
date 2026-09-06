using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a TrustLevel to a representative color.
/// </summary>
public class TrustLevelToColorConverter : IValueConverter
{
    /// <summary>
    /// Gets a static instance of the converter.
    /// </summary>
    public static readonly TrustLevelToColorConverter Instance = new();

    /// <summary>
    /// Converts a TrustLevel to a representative color.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The converter parameter.</param>
    /// <param name="culture">The culture info.</param>
    /// <returns>A brush representing the trust level.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TrustLevel trustLevel)
        {
            return trustLevel switch
            {
                TrustLevel.Trusted => Brushes.Green,
                TrustLevel.Verified => Brushes.SkyBlue,
                TrustLevel.Untrusted => Brushes.Gray,
                _ => Brushes.Gray,
            };
        }

        return Brushes.Gray;
    }

    /// <summary>
    /// Not implemented.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The converter parameter.</param>
    /// <param name="culture">The culture info.</param>
    /// <returns>Nothing.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
