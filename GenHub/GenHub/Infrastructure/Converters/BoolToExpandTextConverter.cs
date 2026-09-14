using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;

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
        var localizationService = ResolveLocalizationService();
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

    private static ILocalizationService? ResolveLocalizationService()
    {
        try
        {
            if (Application.Current?.TryGetResource(LocalizationConstants.ResourceServiceKey, null, out var res) == true &&
                res is ILocalizationService service)
            {
                return service;
            }
        }
        catch
        {
            // Fallback safely if application context is not yet available
        }

        return null;
    }
}
