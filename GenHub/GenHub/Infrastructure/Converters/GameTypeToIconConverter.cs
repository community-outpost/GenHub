using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a <see cref="GameType"/> value to an icon resource URI (used by Image.Source).
/// </summary>
public class GameTypeToIconConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared instance of the <see cref="GameTypeToIconConverter"/>.
    /// </summary>
    public static readonly GameTypeToIconConverter Instance = new();

    /// <summary>
    /// Converts the supplied <see cref="GameType"/> to a string URI pointing to the icon asset.
    /// </summary>
    /// <param name="value">The value produced by the binding source.</param>
    /// <param name="targetType">The type of the binding target property.</param>
    /// <param name="parameter">An optional parameter to be used in the converter logic.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>
    /// A string containing an "avares://" URI for the icon image. If the input is not a valid
    /// <see cref="GameType"/>, a default icon URI is returned.
    /// </returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GameType gt)
        {
            return gt switch
            {
                GameType.Generals => UriConstants.GeneralsIconUri,
                GameType.ZeroHour => UriConstants.ZeroHourIconUri,
                _ => UriConstants.DefaultIconUri,
            };
        }

        return UriConstants.DefaultIconUri;
    }

    /// <summary>
    /// Not supported. This converter does not provide conversion from target back to source.
    /// </summary>
    /// <param name="value">The value produced by the binding target.</param>
    /// <param name="targetType">The type to convert to.</param>
    /// <param name="parameter">An optional parameter to be used in the converter logic.</param>
    /// <param name="culture">The culture to use in the converter.</param>
    /// <returns>This method does not return a value; it always throws <see cref="NotSupportedException"/>.</returns>
    /// <exception cref="NotSupportedException">Always thrown as this converter only supports one-way conversion.</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
