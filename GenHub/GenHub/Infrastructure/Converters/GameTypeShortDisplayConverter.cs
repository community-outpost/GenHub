using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts GameType enum values to short display names (for UI labels).
/// </summary>
public class GameTypeShortDisplayConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared instance of the <see cref="GameTypeShortDisplayConverter"/>.
    /// </summary>
    public static readonly GameTypeShortDisplayConverter Instance = new();

    /// <summary>
    /// Converts a GameType value to a short display string.
    /// </summary>
    /// <param name="value">The GameType value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>A short string representation of the game type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GameType gameType)
        {
            return gameType switch
            {
                GameType.Generals => GameClientConstants.GeneralsShortName,
                GameType.ZeroHour => GameClientConstants.ZeroHourShortName,
                _ => gameType.ToString(),
            };
        }

        return value?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Not supported for one-way conversion.
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the converter.</param>
    /// <returns>This method does not return a value; it always throws <see cref="NotSupportedException"/>.</returns>
    /// <exception cref="NotSupportedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
