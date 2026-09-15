using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// Resolves which CAS pool to use based on content type.
/// </summary>
public class CasPoolResolver(
    IOptions<CasConfiguration> config,
    IUserSettingsService userSettingsService,
    IStorageWritabilityProbe writabilityProbe,
    ILogger<CasPoolResolver> logger) : ICasPoolResolver
{
    /// <summary>
    /// Content types that should use the installation pool (same drive as game).
    /// </summary>
    private static readonly HashSet<ContentType> InstallationPoolTypes =
    [
        ContentType.GameInstallation,
        ContentType.GameClient,
        ContentType.Addon,
        ContentType.Patch,
        ContentType.Map,
        ContentType.Mod,
    ];

    private readonly CasConfiguration _config = config.Value;
    private readonly ConcurrentDictionary<string, bool> _unwritablePoolsLogged = new(PathHelper.PathComparer);

    /// <inheritdoc/>
    public CasPoolType ResolvePool(ContentType contentType)
    {
        var isAvailable = IsInstallationPoolAvailable();
        logger.LogDebug("ResolvePool for {ContentType}: InstallationPoolAvailable={IsAvailable}", contentType, isAvailable);

        // GameInstallation and GameClient go to installation pool for hardlink support
        if (InstallationPoolTypes.Contains(contentType) && isAvailable)
        {
            logger.LogDebug("Resolved {ContentType} to Installation pool", contentType);
            return CasPoolType.Installation;
        }

        // All other content goes to primary pool
        logger.LogDebug("Resolved {ContentType} to Primary pool", contentType);
        return CasPoolType.Primary;
    }

    /// <inheritdoc/>
    public string GetPoolRootPath(CasPoolType poolType)
    {
        return poolType switch
        {
            CasPoolType.Installation when IsInstallationPoolAvailable()
                => GetInstallationPoolRootPath(),
            _ => _config.CasRootPath,
        };
    }

    /// <inheritdoc/>
    public string GetPoolRootPath(ContentType contentType)
    {
        var poolType = ResolvePool(contentType);
        return GetPoolRootPath(poolType);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetLegacyInstallationPoolRootPaths()
    {
        var configuration = userSettingsService.Get().CasConfiguration;
        var roots = new List<string>();

        foreach (var configuredRoot in configuration.LegacyInstallationPoolRootPaths)
        {
            AddRoot(roots, configuredRoot);
        }

        // A configured pool that exists but cannot be written has not been migrated yet, so it
        // still holds the only copy of any object written before it became unwritable.
        var currentPath = configuration.InstallationPoolRootPath;
        if (!string.IsNullOrWhiteSpace(currentPath) &&
            Directory.Exists(currentPath) &&
            !writabilityProbe.CanCreateStorageAt(currentPath))
        {
            AddRoot(roots, currentPath);
        }

        return roots;
    }

    /// <inheritdoc/>
    public bool IsInstallationPoolAvailable()
    {
        var path = GetInstallationPoolRootPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (writabilityProbe.CanCreateStorageAt(path))
        {
            return true;
        }

        if (_unwritablePoolsLogged.TryAdd(path, true))
        {
            logger.LogWarning(
                "Installation CAS pool {PoolPath} is not writable; content will use the primary pool",
                path);
        }

        return false;
    }

    private static void AddRoot(List<string> roots, string? root)
    {
        if (!string.IsNullOrWhiteSpace(root) && !roots.Contains(root, PathHelper.PathComparer))
        {
            roots.Add(root);
        }
    }

    /// <summary>
    /// Gets the installation pool root path from UserSettings.
    /// Always reads current value from UserSettings (not cached).
    /// </summary>
    private string GetInstallationPoolRootPath()
    {
        var userSettings = userSettingsService.Get();
        return userSettings.CasConfiguration.InstallationPoolRootPath;
    }
}
