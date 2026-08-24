using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Provides resource-based localization with English fallback and live binding notifications.
/// </summary>
internal sealed class LocalizationService(
    LocalizationResources resources,
    ILogger<LocalizationService> logger) : ILocalizationService
{
    private static readonly PropertyChangedEventArgs CurrentCultureChangedEventArgs = new(nameof(CurrentCulture));
    private static readonly PropertyChangedEventArgs IndexerChangedEventArgs = new(LocalizationConstants.IndexerPropertyName);

    private readonly IReadOnlyList<CultureInfo> _availableCultures = DiscoverAvailableCultures(resources, logger);
    private readonly object _cultureLock = new();

    /// <inheritdoc/>
    public IReadOnlyList<CultureInfo> AvailableCultures => _availableCultures;

    /// <inheritdoc/>
    public CultureInfo CurrentCulture { get; private set; } = resources.DefaultCulture;

    /// <inheritdoc/>
    public string this[string key] => GetString(key);

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public string GetString(string key, params object[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(arguments);

        CultureInfo culture;
        lock (_cultureLock)
        {
            culture = CurrentCulture;
        }

        try
        {
            var value = resources.ResourceManager.GetString(key, culture);
            if (value is null)
            {
                logger.LogWarning(
                    "Localization resource '{ResourceKey}' was not found for culture '{CultureName}' or its English fallback",
                    key,
                    culture.Name);
                return key;
            }

            if (arguments.Length == 0)
            {
                return value;
            }

            try
            {
                return string.Format(culture, value, arguments);
            }
            catch (FormatException ex)
            {
                logger.LogError(ex, "Localization resource '{ResourceKey}' contains an invalid format string", key);
                return value;
            }
        }
        catch (MissingManifestResourceException ex)
        {
            logger.LogError(ex, "The default localization resource set could not be loaded");
            return key;
        }
        catch (MissingSatelliteAssemblyException ex)
        {
            logger.LogError(ex, "The fallback localization satellite assembly could not be loaded");
            return key;
        }
    }

    /// <inheritdoc/>
    public OperationResult SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        var availableCulture = AvailableCultures.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, culture.Name, StringComparison.OrdinalIgnoreCase));
        if (availableCulture is null)
        {
            return OperationResult.CreateFailure($"Culture '{culture.Name}' is not available.");
        }

        var cultureChanged = false;
        lock (_cultureLock)
        {
            cultureChanged = !string.Equals(
                CurrentCulture.Name,
                availableCulture.Name,
                StringComparison.OrdinalIgnoreCase);
            CurrentCulture = ApplyThreadCulture(availableCulture);
        }

        if (cultureChanged)
        {
            PropertyChanged?.Invoke(this, CurrentCultureChangedEventArgs);
            PropertyChanged?.Invoke(this, IndexerChangedEventArgs);
        }

        return OperationResult.CreateSuccess();
    }

    private static CultureInfo ApplyThreadCulture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        return culture;
    }

    private static IReadOnlyList<CultureInfo> DiscoverAvailableCultures(
        LocalizationResources localizationResources,
        ILogger<LocalizationService> localizationLogger)
    {
        var cultures = new List<CultureInfo> { localizationResources.DefaultCulture };
        var cultureNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            localizationResources.DefaultCulture.Name,
        };

        try
        {
            var directories = Directory.GetDirectories(localizationResources.BaseDirectory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var directory in directories)
            {
                var satelliteAssemblyPath = Path.Combine(directory, localizationResources.SatelliteAssemblyFileName);
                if (!File.Exists(satelliteAssemblyPath))
                {
                    continue;
                }

                var cultureName = Path.GetFileName(directory);
                try
                {
                    var culture = CultureInfo.GetCultureInfo(cultureName);
                    if (localizationResources.ResourceManager.GetResourceSet(
                            culture,
                            createIfNotExists: true,
                            tryParents: false) is null)
                    {
                        localizationLogger.LogWarning(
                            "Ignoring satellite assembly without the GenHub string resource set for culture '{CultureName}'",
                            culture.Name);
                        continue;
                    }

                    if (cultureNames.Add(culture.Name))
                    {
                        cultures.Add(culture);
                    }
                }
                catch (CultureNotFoundException)
                {
                    localizationLogger.LogWarning(
                        "Ignoring localization directory with invalid culture name '{CultureName}'",
                        cultureName);
                }
                catch (MissingManifestResourceException ex)
                {
                    localizationLogger.LogWarning(
                        ex,
                        "Ignoring satellite assembly with missing localization resources for culture '{CultureName}'",
                        cultureName);
                }
                catch (MissingSatelliteAssemblyException ex)
                {
                    localizationLogger.LogWarning(
                        ex,
                        "Ignoring missing localization satellite assembly for culture '{CultureName}'",
                        cultureName);
                }
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            localizationLogger.LogWarning(ex, "Localization base directory was not found");
        }
        catch (IOException ex)
        {
            localizationLogger.LogWarning(ex, "Failed to scan localization satellite assemblies");
        }
        catch (UnauthorizedAccessException ex)
        {
            localizationLogger.LogWarning(ex, "Access was denied while scanning localization satellite assemblies");
        }

        return cultures.AsReadOnly();
    }
}
