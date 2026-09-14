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
}
