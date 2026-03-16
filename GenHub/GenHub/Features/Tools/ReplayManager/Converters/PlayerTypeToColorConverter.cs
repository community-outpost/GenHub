using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Features.Tools.ReplayManager.Converters;

/// <summary>
/// Converts PlayerType enum to a color brush.
/// </summary>
public class PlayerTypeToColorConverter : IValueConverter
{
    /// <summary>
    /// Converts a PlayerType value to a SolidColorBrush.
    /// </summary>
    /// <param name="value">The PlayerType value to convert.</param>
    /// <param name="targetType">The target type (not used).</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="culture">Culture information (not used).</param>
    /// <returns>A SolidColorBrush representing the player type color.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlayerType playerType)
        {
            return playerType switch
            {
                PlayerType.Human => new SolidColorBrush(Color.Parse("#2196F3")),
                PlayerType.Computer => new SolidColorBrush(Color.Parse("#FF9800")),
                PlayerType.Observer => new SolidColorBrush(Color.Parse("#9E9E9E")),
                _ => new SolidColorBrush(Colors.Gray),
            };
        }

        return new SolidColorBrush(Colors.Gray);
    }

    /// <summary>
    /// Converts back from brush to PlayerType (not implemented).
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">Optional parameter.</param>
    /// <param name="culture">Culture information.</param>
    /// <returns>Not implemented.</returns>
    /// <exception cref="NotImplementedException">This conversion is not supported.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
