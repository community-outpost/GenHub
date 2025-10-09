using System.Globalization;
using System.Reflection;
using System.Resources;
using GenHub.Core.Interfaces.Localization;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Localization;

/// <summary>
/// Provides language discovery and resource manager provisioning for localization.
/// </summary>
public class LanguageProvider : ILanguageProvider
{
    private readonly ILogger<LanguageProvider> _logger;
    private readonly Dictionary<string, ResourceManager> _resourceManagers;
    private IReadOnlyList<CultureInfo>? _cachedCultures;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public LanguageProvider(ILogger<LanguageProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceManagers = new Dictionary<string, ResourceManager>();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CultureInfo>> DiscoverAvailableLanguages()
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                if (_cachedCultures != null)
                {
                    _logger.LogDebug("Returning cached cultures: {Count} languages", _cachedCultures.Count);
                    return _cachedCultures;
                }

                var cultures = new List<CultureInfo>();
                var assembly = Assembly.GetExecutingAssembly();

                try
                {
                    // Add default/invariant culture (English)
                    var defaultCulture = CultureInfo.GetCultureInfo("en");
                    cultures.Add(defaultCulture);
                    _logger.LogDebug("Added default culture: {Culture}", defaultCulture.Name);

                    // Discover satellite assemblies
                    var satelliteCultures = DiscoverSatelliteAssemblyCultures(assembly);
                    cultures.AddRange(satelliteCultures);

                    // Remove duplicates and sort by English name
                    _cachedCultures = cultures
                        .DistinctBy(c => c.TwoLetterISOLanguageName)
                        .OrderBy(c => c.EnglishName)
                        .ToList()
                        .AsReadOnly();

                    _logger.LogInformation(
                        "Discovered {Count} available languages: {Languages}",
                        _cachedCultures.Count,
                        string.Join(", ", _cachedCultures.Select(c => c.Name)));

                    return _cachedCultures;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error discovering available languages");
                    
                    // Return at least the default culture
                    _cachedCultures = new List<CultureInfo> { CultureInfo.GetCultureInfo("en") }.AsReadOnly();
                    return _cachedCultures;
                }
            }
        });
    }

    /// <inheritdoc/>
    public ResourceManager GetResourceManager(string baseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName, nameof(baseName));

        lock (_lock)
        {
            if (_resourceManagers.TryGetValue(baseName, out var existingManager))
            {
                return existingManager;
            }

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceManager = new ResourceManager(baseName, assembly);
                
                _resourceManagers[baseName] = resourceManager;
                _logger.LogDebug("Created ResourceManager for base name: {BaseName}", baseName);
                
                return resourceManager;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating ResourceManager for base name: {BaseName}", baseName);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool ValidateCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture, nameof(culture));

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            
            // The invariant/default culture is always valid
            if (culture.Equals(CultureInfo.InvariantCulture) || 
                culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Try to get the satellite assembly for this culture
            try
            {
                var satelliteAssembly = assembly.GetSatelliteAssembly(culture);
                return satelliteAssembly != null;
            }
            catch (FileNotFoundException)
            {
                // Satellite assembly doesn't exist for this culture
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error validating culture: {Culture}", culture.Name);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating culture: {Culture}", culture.Name);
            return false;
        }
    }

    /// <summary>
    /// Discovers cultures that have satellite assemblies.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>A list of cultures with satellite assemblies.</returns>
    private List<CultureInfo> DiscoverSatelliteAssemblyCultures(Assembly assembly)
    {
        var satelliteCultures = new List<CultureInfo>();

        try
        {
            // Get all cultures and check if satellite assembly exists
            var allCultures = CultureInfo.GetCultures(CultureTypes.AllCultures)
                .Where(c => !string.IsNullOrEmpty(c.Name)); // Skip invariant culture

            foreach (var culture in allCultures)
            {
                try
                {
                    // Attempt to get satellite assembly
                    var satelliteAssembly = assembly.GetSatelliteAssembly(culture);
                    if (satelliteAssembly != null)
                    {
                        satelliteCultures.Add(culture);
                        _logger.LogDebug("Found satellite assembly for culture: {Culture}", culture.Name);
                    }
                }
                catch (FileNotFoundException)
                {
                    // This is expected for cultures without satellite assemblies
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Error checking satellite assembly for culture: {Culture}", culture.Name);
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during satellite assembly discovery");
        }

        return satelliteCultures;
    }
}