using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Container for converters that map <see cref="ContentType"/> values to badge visuals.
/// This file contains nested converter types to satisfy the rule that a file contains a single top-level type.
/// </summary>
public static class SourceTypeToBadgeConverters
{
    /// <summary>
    /// Converts a <see cref="ContentType"/> to a <see cref="SolidColorBrush"/> suitable for a badge background.
    /// </summary>
    public class SourceTypeToBadgeBackgroundConverter : IValueConverter
    {
        /// <summary>
        /// Converts the supplied <see cref="ContentType"/> into a <see cref="SolidColorBrush"/>.
        /// </summary>
        /// <param name="value">The value produced by the binding source.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">An optional parameter to be used in the converter logic.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>A <see cref="SolidColorBrush"/> representing the badge background for the given content type,
        /// or a default grey brush if the input is not a <see cref="ContentType"/>.</returns>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ContentType ct)
            {
                return ct switch
                {
                    ContentType.GameClient => new SolidColorBrush(Color.Parse("#FFF9A825")), // amber for CAS
                    ContentType.Mod => new SolidColorBrush(Color.Parse("#FF90CAF9")),
                    _ => new SolidColorBrush(Color.Parse("#FFB2FF59")),
                };
            }

            return new SolidColorBrush(Color.Parse("#FFBDBDBD"));
        }

        /// <summary>
        /// Not supported. Conversion from target back to source is not implemented.
        /// </summary>
        /// <param name="value">The value produced by the binding target.</param>
        /// <param name="targetType">The type to convert to.</param>
        /// <param name="parameter">An optional parameter.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>Throws <see cref="NotSupportedException"/>.</returns>
        /// <exception cref="NotSupportedException">Always thrown as this converter only supports one-way conversion.</exception>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts a <see cref="ContentType"/> to a short text label to display on a badge.
    /// </summary>
    public class SourceTypeToBadgeTextConverter : IValueConverter
    {
        /// <summary>
        /// Converts the supplied <see cref="ContentType"/> into a short badge text.
        /// </summary>
        /// <param name="value">The value produced by the binding source.</param>
        /// <param name="targetType">The type of the binding target property.</param>
        /// <param name="parameter">An optional parameter to be used in the converter logic.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>A short label string such as "CAS", "Mod", or "Local". Returns GameClientConstants.UnknownVersion if input is not a <see cref="ContentType"/>.</returns>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is ContentType ct)
            {
                return ct switch
                {
                    ContentType.GameClient => "CAS",
                    ContentType.Mod => "Mod",
                    _ => "Local",
                };
            }

            return GameClientConstants.UnknownVersion;
        }

        /// <summary>
        /// Not supported. Conversion from target back to source is not implemented.
        /// </summary>
        /// <param name="value">The value produced by the binding target.</param>
        /// <param name="targetType">The type to convert to.</param>
        /// <param name="parameter">An optional parameter.</param>
        /// <param name="culture">The culture to use in the converter.</param>
        /// <returns>Throws <see cref="NotSupportedException"/>.</returns>
        /// <exception cref="NotSupportedException">Always thrown as this converter only supports one-way conversion.</exception>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}