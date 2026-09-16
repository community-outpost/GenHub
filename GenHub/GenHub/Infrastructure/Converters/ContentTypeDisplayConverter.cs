using Avalonia.Data.Converters;
using GenHub.Core.Extensions;
using GenHub.Core.Models.Enums;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts ContentType enum values to user-friendly localized display names.
/// </summary>
public class ContentTypeDisplayConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared instance of the <see cref="ContentTypeDisplayConverter"/>.
    /// </summary>
    public static readonly ContentTypeDisplayConverter Instance = new();

    /// <summary>
    /// Converts a ContentType value to a user-friendly localized display string.
    /// </summary>
    /// <param name="value">The ContentType value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>A user-friendly localized string representation of the content type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ContentType contentType)
        {
            var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
            var key = $"ContentType.{contentType}";
            var localized = localizationService?.GetString(key);
            if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
            {
                return localized;
            }

            return contentType.GetDisplayName();
        }

        return value?.ToString() ?? string.Empty;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
