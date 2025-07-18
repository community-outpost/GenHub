using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Service for discovering and indexing manifests in the GenHub file system, and for populating the manifest cache.
/// </summary>
public class ManifestDiscoveryService(ILogger<ManifestDiscoveryService> logger, IManifestCache manifestCache)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ILogger<ManifestDiscoveryService> _logger = logger;
    private readonly IManifestCache _manifestCache = manifestCache;

    /// <summary>
    /// Gets manifests by content type.
    /// </summary>
    /// <param name="manifests">Manifest dictionary.</param>
    /// <param name="contentType">Content type.</param>
    /// <returns>Enumerable of manifests.</returns>
    public static IEnumerable<GameManifest> GetManifestsByType(
        Dictionary<string, GameManifest> manifests,
        ContentType contentType)
    {
        return manifests.Values.Where(m => m.ContentType == contentType);
    }

    /// <summary>
    /// Gets compatible manifests for a game type.
    /// </summary>
    /// <param name="manifests">Manifest dictionary.</param>
    /// <param name="gameType">Game type.</param>
    /// <returns>Enumerable of compatible manifests.</returns>
    public static IEnumerable<GameManifest> GetCompatibleManifests(
        Dictionary<string, GameManifest> manifests,
        GameType gameType)
    {
        return manifests.Values.Where(m => m.TargetGame == gameType);
    }

    /// <summary>
    /// Discovers manifests in the specified directories and returns them as a dictionary.
    /// </summary>
    /// <param name="searchDirectories">The directories to search for manifests.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A dictionary of discovered manifests keyed by their ID.</returns>
    public async Task<Dictionary<string, GameManifest>> DiscoverManifestsAsync(
        IEnumerable<string> searchDirectories,
        CancellationToken cancellationToken = default)
    {
        var manifests = new Dictionary<string, GameManifest>();
        foreach (var directory in searchDirectories.Where(Directory.Exists))
        {
            _logger.LogInformation("Scanning directory for manifests: {Directory}", directory);
            var manifestFiles = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories);
            foreach (var manifestFile in manifestFiles)
            {
                try
                {
                    var manifest = await LoadManifestAsync(
                        manifestFile,
                        cancellationToken);
                    if (manifest != null)
                    {
                        manifests[manifest.Id] = manifest;
                        _logger.LogDebug(
                            "Discovered manifest: {ManifestId} ({ContentType})",
                            manifest.Id,
                            manifest.ContentType);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load manifest from {ManifestFile}", manifestFile);
                }
            }
        }

        _logger.LogInformation("Discovery completed. Found {ManifestCount} manifests", manifests.Count);
        return manifests;
    }

    /// <summary>
    /// Initializes the manifest cache by discovering manifests from all configured sources.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeCacheAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing manifest cache...");
        await DiscoverEmbeddedManifestsAsync(cancellationToken);

        var localManifestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GenHub",
            "Manifests");
        await DiscoverFileSystemManifestsAsync([localManifestDir], cancellationToken);

        _logger.LogInformation("Manifest cache initialization complete. Loaded {Count} manifests.", _manifestCache.GetAllManifests().Count());
    }

    /// <summary>
    /// Validates manifest dependencies.
    /// </summary>
    /// <param name="manifest">Manifest to validate.</param>
    /// <param name="availableManifests">Available manifests.</param>
    /// <returns>True if dependencies are valid; otherwise, false.</returns>
    public bool ValidateDependencies(
        GameManifest manifest,
        Dictionary<string, GameManifest> availableManifests)
    {
        foreach (var dependency in manifest.Dependencies.Where(d => d.InstallBehavior == DependencyInstallBehavior.RequireExisting || d.InstallBehavior == DependencyInstallBehavior.AutoInstall))
        {
            if (!availableManifests.TryGetValue(dependency.Id, out GameManifest? dependencyManifest))
            {
                _logger.LogWarning(
                    "Missing required dependency {DependencyId} for manifest {ManifestId}",
                    dependency.Id,
                    manifest.Id);
                return false;
            }

            if (!IsVersionCompatible(
                dependencyManifest.Version,
                dependency.MinVersion,
                dependency.MaxVersion))
            {
                _logger.LogWarning(
                    "Dependency {DependencyId} version {Version} is not compatible with required range {MinVersion}-{MaxVersion}",
                    dependency.Id,
                    dependencyManifest.Version,
                    dependency.MinVersion,
                    dependency.MaxVersion);
                return false;
            }
        }

        return true;
    }

    private static bool IsVersionCompatible(string actualVersion, string minVersion, string maxVersion)
    {
        if (!string.IsNullOrEmpty(minVersion) && string.Compare(actualVersion, minVersion, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(maxVersion) && string.Compare(actualVersion, maxVersion, StringComparison.OrdinalIgnoreCase) > 0)
        {
            return false;
        }

        return true;
    }

    private static async Task<GameManifest?> LoadManifestAsync(string manifestPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<GameManifest>(stream, JsonOptions, cancellationToken);
        if (manifest != null && !string.IsNullOrEmpty(manifest.Id))
        {
            return manifest;
        }

        return null;
    }

    private async Task DiscoverFileSystemManifestsAsync(IEnumerable<string> searchDirectories, CancellationToken cancellationToken)
    {
        foreach (var directory in searchDirectories.Where(Directory.Exists))
        {
            _logger.LogInformation("Scanning directory for manifests: {Directory}", directory);
            var manifestFiles = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories);
            foreach (var manifestFile in manifestFiles)
            {
                try
                {
                    await using var stream = File.OpenRead(manifestFile);
                    var manifest = await JsonSerializer.DeserializeAsync<GameManifest>(stream, JsonOptions, cancellationToken);
                    if (manifest != null && !string.IsNullOrEmpty(manifest.Id))
                    {
                        _manifestCache.AddOrUpdateManifest(manifest);
                        _logger.LogDebug("Discovered file system manifest: {ManifestId}", manifest.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load manifest from {ManifestFile}", manifestFile);
                }
            }
        }
    }

    private async Task DiscoverEmbeddedManifestsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Scanning for embedded manifests...");
        var assembly = Assembly.GetExecutingAssembly();
        var manifestResourceNames = assembly.GetManifestResourceNames()
            .Where(r => r.StartsWith("GenHub.Manifests.") && r.EndsWith(".json"));

        foreach (var resourceName in manifestResourceNames)
        {
            try
            {
                await using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                var manifest = await JsonSerializer.DeserializeAsync<GameManifest>(stream, JsonOptions, cancellationToken);
                if (manifest != null && !string.IsNullOrEmpty(manifest.Id))
                {
                    _manifestCache.AddOrUpdateManifest(manifest);
                    _logger.LogDebug("Discovered embedded manifest: {ManifestId}", manifest.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load embedded manifest from {ResourceName}", resourceName);
            }
        }
    }
}
