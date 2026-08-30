using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a boolean selection state to a background brush for selectable cards.
/// </summary>
public class BoolToBackgroundConverter : IValueConverter
{
    private static readonly IBrush Selected = new SolidColorBrush(Color.FromArgb(60, 171, 71, 188));
    private static readonly IBrush Unselected = new SolidColorBrush(Color.Parse("#252525"));

    /// <summary>
    /// Converts a boolean to the matching background brush.
    /// </summary>
    /// <param name="value">The boolean value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>A <see cref="IBrush"/> for the selected or unselected state.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Selected : Unselected;

    /// <summary>
    /// Converts back from a brush to a boolean. Not implemented.
    /// </summary>
    /// <param name="value">The brush value to convert back.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">Optional parameter for conversion.</param>
    /// <param name="culture">The culture to use for conversion.</param>
    /// <returns>This method is not implemented and always throws.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
