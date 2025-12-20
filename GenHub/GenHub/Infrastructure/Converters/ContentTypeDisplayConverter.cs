using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Extensions;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts ContentType enum values to user-friendly display names.
/// </summary>
public class ContentTypeDisplayConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared instance of the <see cref="ContentTypeDisplayConverter"/>.
    /// </summary>
    public static readonly ContentTypeDisplayConverter Instance = new();

    /// <summary>
    /// Converts a ContentType value to a user-friendly display string.
    /// </summary>
    /// <param name="value">The ContentType value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>A user-friendly string representation of the content type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ContentType contentType)
        {
            return contentType.GetDisplayName();
        }

        return value?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Not supported for one-way conversion.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>This method does not return a value; it always throws <see cref="NotSupportedException"/>.</returns>
    /// <exception cref="NotSupportedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
