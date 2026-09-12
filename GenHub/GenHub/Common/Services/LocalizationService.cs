using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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

    private readonly IReadOnlyList<CultureInfo> _availableCultures =
        LocalizationCultureUtilities.DiscoverAvailableCultures(resources, logger);

    private readonly object _cultureLock = new();
    private readonly ConcurrentDictionary<(string CultureName, string ResourceKey), byte> _missingResourceWarnings = new();

    /// <inheritdoc/>
    public IReadOnlyList<CultureInfo> AvailableCultures => _availableCultures;

    /// <inheritdoc/>
    public CultureInfo CurrentCulture { get; private set; } =
        LocalizationCultureUtilities.ApplyUiCulture(resources.DefaultCulture);

    /// <inheritdoc/>
    public string this[string key] => GetString(key);

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public string GetString(string key, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(arguments);

        var culture = CurrentCulture;

        try
        {
            var value = resources.ResourceManager.GetString(key, culture);
            if (value is null)
            {
                if (_missingResourceWarnings.TryAdd((culture.Name, key), 0))
                {
                    logger.LogWarning(
                        "Localization resource '{ResourceKey}' was not found for culture '{CultureName}' or its English fallback",
                        key,
                        culture.Name);
                }

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
            CurrentCulture = LocalizationCultureUtilities.ApplyUiCulture(availableCulture);
        }

        if (cultureChanged)
        {
            PropertyChanged?.Invoke(this, CurrentCultureChangedEventArgs);
            PropertyChanged?.Invoke(this, IndexerChangedEventArgs);
        }

        return OperationResult.CreateSuccess();
    }
}
