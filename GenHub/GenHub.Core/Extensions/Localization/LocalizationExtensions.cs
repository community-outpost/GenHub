using System.Globalization;
using GenHub.Core.Interfaces.Localization;
using GenHub.Core.Resources.Strings;

namespace GenHub.Core.Extensions.Localization;

/// <summary>
/// Extension methods for localization services and culture-aware formatting.
/// </summary>
public static class LocalizationExtensions
{
    /// <summary>
    /// Tries to get a localized string, returning a success indicator.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="value">The localized string if found; otherwise, null.</param>
    /// <returns>True if the string was found; otherwise, false.</returns>
    public static bool TryGetString(
        this ILocalizationService service,
        string key,
        out string? value)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));
        ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(key));

        try
        {
            var result = service.GetString(StringResources.UiCommon, key);

            // Check if we got a missing translation marker
            if (result.StartsWith('[') && result.EndsWith(']'))
            {
                value = null;
                return false;
            }

            value = result;
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Formats a date using the current culture's date format.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <param name="date">The date to format.</param>
    /// <param name="format">Optional format string (defaults to short date pattern).</param>
    /// <returns>The formatted date string.</returns>
    public static string FormatDate(
        this ILocalizationService service,
        DateTime date,
        string? format = null)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        var culture = service.CurrentCulture;
        return string.IsNullOrWhiteSpace(format)
            ? date.ToString("d", culture)  // Short date pattern
            : date.ToString(format, culture);
    }

    /// <summary>
    /// Formats a date and time using the current culture's format.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <param name="dateTime">The date and time to format.</param>
    /// <param name="format">Optional format string (defaults to short date + time pattern).</param>
    /// <returns>The formatted date and time string.</returns>
    public static string FormatDateTime(
        this ILocalizationService service,
        DateTime dateTime,
        string? format = null)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        var culture = service.CurrentCulture;
        return string.IsNullOrWhiteSpace(format)
            ? dateTime.ToString("g", culture)  // Short date + time pattern
            : dateTime.ToString(format, culture);
    }

    /// <summary>
    /// Formats a time using the current culture's time format.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <param name="time">The time to format.</param>
    /// <param name="format">Optional format string (defaults to short time pattern).</param>
    /// <returns>The formatted time string.</returns>
    public static string FormatTime(
        this ILocalizationService service,
        DateTime time,
        string? format = null)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        var culture = service.CurrentCulture;
        return string.IsNullOrWhiteSpace(format)
            ? time.ToString("t", culture)  // Short time pattern
            : time.ToString(format, culture);
    }

    /// <summary>
    /// Formats a number using the current culture's number format.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <param name="number">The number to format.</param>
    /// <param name="decimals">Optional number of decimal places.</param>
    /// <returns>The formatted number string.</returns>
    public static string FormatNumber(
        this ILocalizationService service,
        double number,
        int? decimals = null)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        var culture = service.CurrentCulture;
        return decimals.HasValue
            ? number.ToString($"N{decimals.Value}", culture)
            : number.ToString("N", culture);
    }

    /// <summary>
    /// Formats a number using the current culture's number format.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <param name="number">The number to format.</param>
    /// <param name="decimals">Optional number of decimal places.</param>
    /// <returns>The formatted number string.</returns>
    public static string FormatNumber(
        this ILocalizationService service,
        decimal number,
        int? decimals = null)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        var culture = service.CurrentCulture;
        return decimals.HasValue
            ? number.ToString($"N{decimals.Value}", culture)
            : number.ToString("N", culture);
    }

    /// <summary>
    /// Formats a currency value using the current culture's currency format.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <param name="amount">The amount to format.</param>
    /// <param name="currencyCode">Optional ISO currency code (e.g., "USD", "EUR"). If null, uses culture's default.</param>
    /// <returns>The formatted currency string.</returns>
    public static string FormatCurrency(
        this ILocalizationService service,
        decimal amount,
        string? currencyCode = null)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        var culture = service.CurrentCulture;

        if (!string.IsNullOrWhiteSpace(currencyCode))
        {
            // Create a region info to get the currency symbol
            try
            {
                var regions = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
                    .Select(c => new RegionInfo(c.Name))
                    .FirstOrDefault(r => r.ISOCurrencySymbol.Equals(currencyCode, StringComparison.OrdinalIgnoreCase));

                if (regions != null)
                {
                    return amount.ToString("C", culture);
                }
            }
            catch
            {
                // Fall through to default formatting
            }
        }

        return amount.ToString("C", culture);
    }

    /// <summary>
    /// Formats a percentage using the current culture's percentage format.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <param name="value">The value to format (e.g., 0.85 for 85%).</param>
    /// <param name="decimals">Optional number of decimal places.</param>
    /// <returns>The formatted percentage string.</returns>
    public static string FormatPercentage(
        this ILocalizationService service,
        double value,
        int? decimals = null)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        var culture = service.CurrentCulture;
        return decimals.HasValue
            ? value.ToString($"P{decimals.Value}", culture)
            : value.ToString("P", culture);
    }

    /// <summary>
    /// Formats a file size in a human-readable format using current culture.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>The formatted file size string (e.g., "1.5 MB").</returns>
    public static string FormatFileSize(
        this ILocalizationService service,
        long bytes)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int order = 0;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        var culture = service.CurrentCulture;
        return $"{size.ToString("0.##", culture)} {sizes[order]}";
    }

    /// <summary>
    /// Gets a formatted string for plural forms based on count.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <param name="count">The count to determine plurality.</param>
    /// <param name="zeroKey">The resource key for zero items.</param>
    /// <param name="oneKey">The resource key for one item.</param>
    /// <param name="manyKey">The resource key for many items.</param>
    /// <returns>The appropriate pluralized string.</returns>
    public static string GetPluralString(
        this ILocalizationService service,
        int count,
        string zeroKey,
        string oneKey,
        string manyKey)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));
        ArgumentException.ThrowIfNullOrWhiteSpace(zeroKey, nameof(zeroKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(oneKey, nameof(oneKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(manyKey, nameof(manyKey));

        var key = count switch
        {
            0 => zeroKey,
            1 => oneKey,
            _ => manyKey
        };

        return service.GetString(StringResources.UiCommon, key, count);
    }

    /// <summary>
    /// Checks if a culture is Right-to-Left (RTL).
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <returns>True if the current culture is RTL; otherwise, false.</returns>
    public static bool IsRightToLeft(this ILocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        var culture = service.CurrentCulture;
        return culture.TextInfo.IsRightToLeft;
    }

    /// <summary>
    /// Gets the display name of the current culture in its native language.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <returns>The native name of the current culture.</returns>
    public static string GetCurrentCultureNativeName(this ILocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        return service.CurrentCulture.NativeName;
    }

    /// <summary>
    /// Gets the display name of the current culture in English.
    /// </summary>
    /// <param name="service">The localization service.</param>
    /// <returns>The English name of the current culture.</returns>
    public static string GetCurrentCultureEnglishName(this ILocalizationService service)
    {
        ArgumentNullException.ThrowIfNull(service, nameof(service));

        return service.CurrentCulture.EnglishName;
    }
}