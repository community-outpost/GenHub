using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Helpers;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a string containing HTML markup to clean, normalized plain text.
/// Optionally accepts a maximum length integer as parameter for single-line truncated conversion.
/// </summary>
public class StripHtmlConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text)
        {
            return value;
        }

        if (parameter is int maxLen)
        {
            return HtmlTextHelper.CleanToSingleLine(text, maxLen);
        }

        if (parameter is string paramStr && int.TryParse(paramStr, CultureInfo.InvariantCulture, out var parsedMax))
        {
            return HtmlTextHelper.CleanToSingleLine(text, parsedMax);
        }

        return HtmlTextHelper.NormalizeHtml(text);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always thrown as two-way binding is not supported.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
