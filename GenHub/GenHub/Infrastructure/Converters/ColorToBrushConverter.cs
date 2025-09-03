using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts color values to brush objects for XAML binding.
/// </summary>
public class ColorToBrushConverter : IValueConverter
    {
        /// <summary>
        /// Converts a color value to a brush object.
        /// </summary>
        /// <param name="value">The color value to convert.</param>
        /// <param name="targetType">The target type for the conversion.</param>
        /// <param name="parameter">Optional parameter for conversion.</param>
        /// <param name="culture">The culture to use for conversion.</param>
        /// <returns>A <see cref="SolidColorBrush"/> object.</returns>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null)
                return new SolidColorBrush(Colors.Transparent);

            try
            {
                // Handle string color values (hex codes)
                if (value is string colorString)
                {
                    if (string.IsNullOrWhiteSpace(colorString))
                        return new SolidColorBrush(Colors.Transparent);

                    if (Color.TryParse(colorString, out var parsedColor))
                        return new SolidColorBrush(parsedColor);
                }

                // Handle Color objects
                if (value is Color color)
                    return new SolidColorBrush(color);

                // Handle numeric values (for opacity, etc.)
                if (value is double opacity && parameter is string paramColor)
                {
                    if (Color.TryParse(paramColor, out var baseColor))
                    {
                        var adjustedColor = Color.FromArgb(
                            (byte)(opacity * 255),
                            baseColor.R,
                            baseColor.G,
                            baseColor.B);
                        return new SolidColorBrush(adjustedColor);
                    }
                }

                // Fallback for unknown types
                return new SolidColorBrush(Colors.Transparent);
            }
            catch
            {
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        /// <summary>
        /// Converts a brush back to a color string (for two-way binding).
        /// </summary>
        /// <param name="value">The brush value to convert back.</param>
        /// <param name="targetType">The target type for the conversion.</param>
        /// <param name="parameter">Optional parameter for conversion.</param>
        /// <param name="culture">The culture to use for conversion.</param>
        /// <returns>A color string representation.</returns>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
                return brush.Color.ToString();

            return null;
        }
    }
