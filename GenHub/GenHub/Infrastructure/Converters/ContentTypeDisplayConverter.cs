using Avalonia.Data.Converters;
using GenHub.Core.Extensions;
using GenHub.Core.Models.Enums;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts ContentType enum values or string representations to user-friendly localized display names.
/// </summary>
public class ContentTypeDisplayConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared instance of the <see cref="ContentTypeDisplayConverter"/>.
    /// </summary>
    public static readonly ContentTypeDisplayConverter Instance = new();

    /// <summary>
    /// Converts a ContentType value or string to a user-friendly localized display string.
    /// </summary>
    /// <param name="value">The ContentType value or string to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>A user-friendly localized string representation of the content type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        ContentType? contentType = value switch
        {
            ContentType ct => ct,
            string str when Enum.TryParse<ContentType>(str, true, out var parsedCt) => parsedCt,
            _ => null,
        };

        if (contentType.HasValue)
        {
            var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
            var key = $"ContentType.{contentType.Value}";
            var localized = localizationService?.GetString(key);
            if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
            {
                return localized;
            }

            return contentType.Value.GetDisplayName();
        }

        return value?.ToString() ?? string.Empty;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
