using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Applies UI cultures and discovers deployed localization satellite assemblies.
/// </summary>
internal static class LocalizationCultureUtilities
{
    /// <summary>
    /// Applies a culture to resource lookup without changing regional parsing or formatting behavior.
    /// </summary>
    /// <param name="culture">The UI culture to apply.</param>
    /// <returns>The applied culture.</returns>
    internal static CultureInfo ApplyUiCulture(CultureInfo culture)
    {
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        return culture;
    }

    /// <summary>
    /// Discovers the neutral culture and valid deployed satellite resource cultures.
    /// </summary>
    /// <param name="resources">The localization resource description.</param>
    /// <param name="logger">The logger used for invalid deployment diagnostics.</param>
    /// <returns>The available cultures in deterministic order.</returns>
    internal static IReadOnlyList<CultureInfo> DiscoverAvailableCultures(
        LocalizationResources resources,
        ILogger logger)
    {
        var cultures = new List<CultureInfo> { resources.DefaultCulture };
        var cultureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            resources.DefaultCulture.Name,
        };

        try
        {
            var directories = Directory.GetDirectories(resources.BaseDirectory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var directory in directories)
            {
                TryAddCulture(directory, resources, logger, cultures, cultureNames);
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            logger.LogWarning(ex, "Localization base directory was not found");
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to scan localization satellite assemblies");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access was denied while scanning localization satellite assemblies");
        }

        return cultures.AsReadOnly();
    }

    private static void TryAddCulture(
        string directory,
        LocalizationResources resources,
        ILogger logger,
        ICollection<CultureInfo> cultures,
        ISet<string> cultureNames)
    {
        var satelliteAssemblyPath = Path.Combine(directory, resources.SatelliteAssemblyFileName);
        if (!File.Exists(satelliteAssemblyPath))
        {
            return;
        }

        var cultureName = Path.GetFileName(directory);
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            if (resources.ResourceManager.GetResourceSet(
                    culture,
                    createIfNotExists: true,
                    tryParents: false) is null)
            {
                logger.LogWarning(
                    "Ignoring satellite assembly without the GenHub string resource set for culture '{CultureName}'",
                    culture.Name);
                return;
            }

            if (cultureNames.Add(culture.Name))
            {
                cultures.Add(culture);
            }
        }
        catch (BadImageFormatException ex)
        {
            logger.LogWarning(ex, "Ignoring invalid localization satellite assembly for culture '{CultureName}'", cultureName);
        }
        catch (CultureNotFoundException ex)
        {
            logger.LogWarning(ex, "Ignoring localization directory with invalid culture name '{CultureName}'", cultureName);
        }
        catch (MissingManifestResourceException ex)
        {
            logger.LogWarning(ex, "Ignoring satellite assembly with missing localization resources for culture '{CultureName}'", cultureName);
        }
        catch (MissingSatelliteAssemblyException ex)
        {
            logger.LogWarning(ex, "Ignoring missing localization satellite assembly for culture '{CultureName}'", cultureName);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Ignoring unreadable localization satellite assembly for culture '{CultureName}'", cultureName);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access was denied to the localization satellite assembly for culture '{CultureName}'", cultureName);
        }
    }
}
