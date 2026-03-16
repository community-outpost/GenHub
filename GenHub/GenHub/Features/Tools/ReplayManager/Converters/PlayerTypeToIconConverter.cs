using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Features.Tools.ReplayManager.Converters;

/// <summary>
/// Converts PlayerType enum to an icon/emoji.
/// </summary>
public class PlayerTypeToIconConverter : IValueConverter
{
    /// <summary>
    /// Converts a PlayerType value to an icon string.
    /// </summary>
    /// <param name="value">The PlayerType value to convert.</param>
    /// <param name="targetType">The target type (not used).</param>
    /// <param name="parameter">Optional parameter (not used).</param>
    /// <param name="culture">Culture information (not used).</param>
    /// <returns>An icon string representing the player type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PlayerType playerType)
        {
            return playerType switch
            {
                PlayerType.Human => "👤",
                PlayerType.Computer => "🤖",
                PlayerType.Observer => "👁️",
                _ => "?",
            };
        }

        return "?";
    }

    /// <summary>
    /// Converts back from icon to PlayerType (not implemented).
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
