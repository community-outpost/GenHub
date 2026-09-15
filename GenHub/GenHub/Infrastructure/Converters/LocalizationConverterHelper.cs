using Avalonia;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Provides shared helper methods for converters requiring localization services.
/// </summary>
internal static class LocalizationConverterHelper
{
    /// <summary>
    /// Resolves the current application's <see cref="ILocalizationService"/> from resources.
    /// </summary>
    /// <returns>The localization service if available; otherwise, null.</returns>
    public static ILocalizationService? ResolveLocalizationService()
    {
        try
        {
            if (Application.Current?.TryGetResource(LocalizationConstants.ResourceServiceKey, null, out var res) == true &&
                res is ILocalizationService service)
            {
                return service;
            }
        }
        catch
        {
            // Resource lookup failure fallback
        }

        return null;
    }

    /// <summary>
    /// Gets a localized string for the specified key, or returns the fallback if missing or echoed as key.
    /// </summary>
    /// <param name="localizationService">The localization service.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The fallback value.</param>
    /// <returns>The localized string if available and not equal to the key; otherwise, the fallback.</returns>
    public static string GetLocalizedOrDefault(ILocalizationService? localizationService, string key, string fallback)
    {
        if (localizationService == null || string.IsNullOrWhiteSpace(key))
        {
            return fallback;
        }

        var localized = localizationService.GetString(key);
        if (!string.IsNullOrEmpty(localized) && !string.Equals(localized, key, System.StringComparison.Ordinal))
        {
            return localized;
        }

        return fallback;
    }
}
