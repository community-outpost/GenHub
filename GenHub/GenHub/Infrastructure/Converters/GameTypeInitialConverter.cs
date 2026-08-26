using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a game type value (enum or its string representation) to a short initial for display.
/// </summary>
public class GameTypeInitialConverter : IValueConverter
{
    /// <summary>
    /// Converts a game type value to its display initial.
    /// </summary>
    /// <param name="value">The game type value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>A short string initial representing the game type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString();

        if (string.IsNullOrEmpty(text))
            return "?";

        return text switch
        {
            "ZeroHour" => "ZH",
            "Generals" => "G",
            _ => text[..1].ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Converts back from an initial to a game type. Not implemented.
    /// </summary>
    /// <param name="value">The value produced by the binding target.</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <param name="parameter">The converter parameter to use.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>This conversion is not supported and always throws.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
