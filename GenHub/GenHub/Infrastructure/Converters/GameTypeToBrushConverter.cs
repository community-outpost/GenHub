using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Enums;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a GameType enum value to a SolidColorBrush for UI display.
/// </summary>
public class GameTypeToBrushConverter : IValueConverter
{
    /// <summary>
    /// Gets the singleton instance of the converter.
    /// </summary>
    public static readonly GameTypeToBrushConverter Instance = new();

    /// <summary>
    /// Converts a GameType to a SolidColorBrush.
    /// </summary>
    /// <param name="value">The GameType value to convert.</param>
    /// <param name="targetType">The target type (ignored).</param>
    /// <param name="parameter">Optional parameter (ignored).</param>
    /// <param name="culture">The culture (ignored).</param>
    /// <returns>A SolidColorBrush representing the game type color.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GameType gameType)
        {
            return gameType switch
            {
                GameType.Generals => new SolidColorBrush(Color.Parse("#BD5A0F")), // Orange for Generals
                GameType.ZeroHour => new SolidColorBrush(Color.Parse("#2D4963")), // Teal for Zero Hour
                _ => new SolidColorBrush(Colors.Gray),
            };
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    /// <summary>
    /// Converts back from a brush to a GameType (not implemented).
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">Optional parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns>Throws NotImplementedException.</returns>
    /// <exception cref="NotImplementedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
