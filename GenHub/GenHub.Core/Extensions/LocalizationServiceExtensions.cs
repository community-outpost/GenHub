using GenHub.Core.Interfaces.Common;
using System;
using System.Globalization;

namespace GenHub.Core.Extensions;

/// <summary>
/// Extension methods for resolving localized strings with English fallbacks.
/// </summary>
public static class LocalizationServiceExtensions
{
    /// <summary>
    /// Gets a localized string by resource key, falling back to the provided English text
    /// when the service is unavailable or the key has no resource. Missing keys do not
    /// produce missing-resource warnings.
    /// </summary>
    /// <param name="localizationService">The localization service, or null when unavailable.</param>
    /// <param name="key">The resource key to resolve.</param>
    /// <param name="fallback">The English text to use when the key cannot be resolved.</param>
    /// <returns>The localized value or the fallback text.</returns>
    public static string GetLocalizedString(this ILocalizationService? localizationService, string key, string fallback)
    {
        if (localizationService != null && localizationService.TryGetString(key, out var result))
        {
            return result;
        }

        return fallback;
    }

    /// <summary>
    /// Gets and formats a localized string by resource key, falling back to the formatted
    /// English text when the service is unavailable or the key has no resource. Missing keys
    /// do not produce missing-resource warnings.
    /// </summary>
    /// <param name="localizationService">The localization service, or null when unavailable.</param>
    /// <param name="key">The resource key to resolve.</param>
    /// <param name="fallback">The English format string to use when the key cannot be resolved.</param>
    /// <param name="args">Format arguments applied to the localized value or the fallback.</param>
    /// <returns>The localized and formatted value or the formatted fallback text.</returns>
    public static string GetLocalizedString(this ILocalizationService? localizationService, string key, string fallback, params object?[] args)
    {
        if (localizationService != null && localizationService.TryGetString(key, out var result, args))
        {
            return result;
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, args);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}
