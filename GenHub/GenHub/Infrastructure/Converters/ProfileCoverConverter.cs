using System;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System.Globalization;

namespace GenHub.GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a string file path to a Bitmap for use as a profile cover image.
/// </summary>
public class ProfileCoverConverter : IValueConverter
{
    /// <summary>
    /// Converts a string file path to a Bitmap for use as a profile cover image.
    /// </summary>
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && !string.IsNullOrWhiteSpace(path))
        {
            try
            {
                return new Bitmap(path);
            }
            catch
            {
            }
        }

        return null;
    }

    /// <summary>
    /// Not implemented. Converts a Bitmap back to a string file path.
    /// </summary>
    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
