using System.Globalization;
using System.Resources;

namespace GenHub.Core.Interfaces.Localization;

/// <summary>
/// Manages resource discovery and resource manager provisioning for localization.
/// </summary>
public interface ILanguageProvider
{
    /// <summary>
    /// Discovers all available satellite assembly cultures.
    /// </summary>
    /// <returns>A task that yields a list of available cultures.</returns>
    Task<IReadOnlyList<CultureInfo>> DiscoverAvailableLanguages();

    /// <summary>
    /// Gets a ResourceManager for the specified base name.
    /// </summary>
    /// <param name="baseName">The base name of the resource (e.g., "GenHub.Core.Resources.Strings").</param>
    /// <returns>The ResourceManager instance.</returns>
    ResourceManager GetResourceManager(string baseName);

    /// <summary>
    /// Validates that a culture is available in the application.
    /// </summary>
    /// <param name="culture">The culture to validate.</param>
    /// <returns>True if the culture is available; otherwise, false.</returns>
    bool ValidateCulture(CultureInfo culture);
}