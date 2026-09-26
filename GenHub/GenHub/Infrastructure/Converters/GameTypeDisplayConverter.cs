using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts GameType enum values or strings to user-friendly localized display names.
/// </summary>
public class GameTypeDisplayConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared instance of the <see cref="GameTypeDisplayConverter"/>.
    /// </summary>
    public static readonly GameTypeDisplayConverter Instance = new();

    /// <summary>
    /// Converts a GameType value or string to a user-friendly localized display string.
    /// </summary>
    /// <param name="value">The GameType value or string to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>A user-friendly localized string representation of the game type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        GameType? gameType = value switch
        {
            GameType gt => gt,
            string str when Enum.TryParse<GameType>(str, true, out var parsedGt) => parsedGt,
            _ => null,
        };

        if (gameType.HasValue)
        {
            var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
            var key = gameType.Value switch
            {
                GameType.Generals => "GameProfiles.GameType.Generals",
                GameType.ZeroHour => "GameProfiles.GameType.ZeroHour",
                _ => null,
            };

            if (key != null)
            {
                var localized = localizationService?.GetString(key);
                if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, key, StringComparison.Ordinal))
                {
                    return localized;
                }
            }

            return gameType.Value switch
            {
                GameType.Generals => GameClientConstants.GeneralsFullName,
                GameType.ZeroHour => GameClientConstants.ZeroHourFullName,
                _ => gameType.Value.ToString(),
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
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>This method does not return a value; it always throws <see cref="NotSupportedException"/>.</returns>
    /// <exception cref="NotSupportedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
