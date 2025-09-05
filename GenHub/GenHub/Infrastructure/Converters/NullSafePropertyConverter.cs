using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Safely handles null values in property binding.
/// </summary>
public class NullSafePropertyConverter : IValueConverter
    {
        /// <summary>
        /// Converts a value while handling nulls safely.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <param name="targetType">The target type for the conversion.</param>
        /// <param name="parameter">Optional parameter for conversion.</param>
        /// <param name="culture">The culture to use for conversion.</param>
        /// <returns>The converted value or a safe fallback.</returns>
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value ?? (parameter?.ToString() ?? string.Empty);
        }

        /// <summary>
        /// Converts a value back while handling nulls safely.
        /// </summary>
        /// <param name="value">The value to convert back.</param>
        /// <param name="targetType">The target type for the conversion.</param>
        /// <param name="parameter">Optional parameter for conversion.</param>
        /// <param name="culture">The culture to use for conversion.</param>
        /// <returns>The converted value.</returns>
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value;
        }
    }
