namespace GenHub.Infrastructure.Converters;

using System;
using System.Globalization;
using Avalonia.Data.Converters;

/// <summary>
/// Converts a string to an integer for CommandParameter binding.
/// </summary>
public class StringToIntConverter : IValueConverter
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly StringToIntConverter Instance = new StringToIntConverter();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is string s && int.TryParse(s, out int result))
        {
            return result;
        }

        if (value is int i)
        {
            return i;
        }

        return 0;
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo? culture)
    {
        if (value is int i)
        {
            return i.ToString();
        }

        return "0";
    }
}