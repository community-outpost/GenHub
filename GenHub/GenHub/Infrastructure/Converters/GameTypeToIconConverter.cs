using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System;
using System.Globalization;

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

/// <summary>
/// Converts ContentType enum values to user-friendly display names.
/// </summary>
public class ContentTypeDisplayConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared instance of the <see cref="ContentTypeDisplayConverter"/>.
    /// </summary>
    public static readonly ContentTypeDisplayConverter Instance = new();

    /// <summary>
    /// Converts a ContentType value to a user-friendly display string.
    /// </summary>
    /// <param name="value">The ContentType value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>A user-friendly string representation of the content type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ContentType contentType)
        {
            return contentType switch
            {
                ContentType.GameInstallation => "Game Installation",
                ContentType.GameClient => "Game Client",
                ContentType.Mod => "Modification",
                ContentType.Patch => "Patch",
                ContentType.Addon => "Add-on",
                ContentType.MapPack => "Map Pack",
                ContentType.LanguagePack => "Language Pack",
                ContentType.ContentBundle => "Content Bundle",
                ContentType.PublisherReferral => "Publisher Referral",
                ContentType.ContentReferral => "Content Referral",
                _ => contentType.ToString()
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

/// <summary>
/// Converts GameType enum values to user-friendly display names.
/// </summary>
public class GameTypeDisplayConverter : IValueConverter
{
    /// <summary>
    /// Gets a shared instance of the <see cref="GameTypeDisplayConverter"/>.
    /// </summary>
    public static readonly GameTypeDisplayConverter Instance = new();

    /// <summary>
    /// Converts a GameType value to a user-friendly display string.
    /// </summary>
    /// <param name="value">The GameType value to convert.</param>
    /// <param name="targetType">The target type for the conversion.</param>
    /// <param name="parameter">An optional parameter for the conversion.</param>
    /// <param name="culture">The culture to use for the conversion.</param>
    /// <returns>A user-friendly string representation of the game type.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GameType gameType)
        {
            return gameType switch
            {
                GameType.Generals => "Command & Conquer: Generals",
                GameType.ZeroHour => "Command & Conquer: Generals Zero Hour",
                _ => gameType.ToString()
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
                GameType.Generals => "Generals",
                GameType.ZeroHour => "Zero Hour",
                _ => gameType.ToString()
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
                GameInstallationType.EaApp => "EA App",
                GameInstallationType.Steam => "Steam",
                GameInstallationType.TheFirstDecade => "The First Decade",
                GameInstallationType.CDISO => "CD-ROM",
                GameInstallationType.Wine => "Wine/Proton",
                GameInstallationType.Retail => "Retail Installation",
                GameInstallationType.Unknown => "Unknown",
                _ => installationType.ToString()
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
