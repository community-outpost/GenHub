using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean to expand/collapse text ("Read More" or "Show Less").
/// </summary>
public class BoolToExpandTextConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean value to expand/collapse text.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns>The text string.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
        var showLess = localizationService?.GetString("Common.ShowLess") ?? "Show Less";
        var readMore = localizationService?.GetString("Common.ReadMore") ?? "Read More";

        if (value is bool isExpanded)
        {
            return isExpanded ? showLess : readMore;
        }

        return readMore;
    }

    /// <summary>
    /// Converts back.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">The parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns>The converted value.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
