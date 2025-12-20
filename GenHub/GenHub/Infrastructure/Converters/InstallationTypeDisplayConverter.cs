using System;
using System.Globalization;
using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts GameInstallationType enum values to user-friendly display names.
/// </summary>
public class InstallationTypeDisplayConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared instance of the <see cref="InstallationTypeDisplayConverter"/>.
    /// </summary>
    public static readonly InstallationTypeDisplayConverter Instance = new();

    /// <summary>
    /// Converts a GameInstallationType value to a user-friendly display string.
    /// </summary>
    /// <param name="value">The GameInstallationType value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>A user-friendly string representation of the installation type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GameInstallationType installationType)
        {
            return installationType switch
            {
                GameInstallationType.EaApp => PublisherInfoConstants.EaApp.Name,
                GameInstallationType.Steam => PublisherInfoConstants.Steam.Name,
                GameInstallationType.TheFirstDecade => PublisherInfoConstants.TheFirstDecade.Name,
                GameInstallationType.CDISO => PublisherInfoConstants.CdIso.Name,
                GameInstallationType.Wine => PublisherInfoConstants.Wine.Name,
                GameInstallationType.Retail => PublisherInfoConstants.Retail.Name,
                GameInstallationType.Unknown => PublisherInfoConstants.Retail.Name, // Default to retail for unknown
                _ => installationType.ToString(),
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
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
