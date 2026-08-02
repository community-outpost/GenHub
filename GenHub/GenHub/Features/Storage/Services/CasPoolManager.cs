using System;
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
/// Manages multiple CAS storage pools for content-type-based routing.
/// </summary>
public class CasPoolManager : ICasPoolManager
{
    private readonly ICasPoolResolver _poolResolver;
    private readonly ILogger<CasPoolManager> _logger;
    private readonly IFileHashProvider _hashProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IStorageWritabilityProbe _writabilityProbe;
    private readonly CasConfiguration _config;
    private readonly ConcurrentDictionary<CasPoolType, ICasStorage> _storages = new();
    private readonly object _initLock = new();
    private string? _installationPoolRoot;
    private volatile ICasStorage? _legacyInstallationStorage;
    private string? _legacyInstallationPoolRoot;

    /// <summary>
    /// Initializes a new instance of the <see cref="CasPoolManager"/> class.
    /// </summary>
    /// <param name="poolResolver">The pool resolver for routing decisions.</param>
    /// <param name="config">The CAS configuration.</param>
    /// <param name="hashProvider">The file hash provider.</param>
    /// <param name="loggerFactory">The logger factory for creating storage loggers.</param>
    /// <param name="writabilityProbe">The storage writability probe.</param>
    /// <param name="logger">The logger instance.</param>
    public CasPoolManager(
        ICasPoolResolver poolResolver,
        IOptions<CasConfiguration> config,
        IFileHashProvider hashProvider,
        ILoggerFactory loggerFactory,
        IStorageWritabilityProbe writabilityProbe,
        ILogger<CasPoolManager> logger)
    {
        _poolResolver = poolResolver;
        _config = config.Value;
        _hashProvider = hashProvider;
        _loggerFactory = loggerFactory;
        _writabilityProbe = writabilityProbe;
        _logger = logger;

        // Initialize primary pool
        InitializePool(CasPoolType.Primary);

        RefreshInstallationPools();
    }

    /// <inheritdoc/>
    public ICasPoolResolver PoolResolver => _poolResolver;

    /// <inheritdoc/>
    public ICasStorage GetStorage(CasPoolType poolType)
    {
        if (_storages.TryGetValue(poolType, out var storage))
        {
            _logger.LogDebug("Returning existing {PoolType} pool storage", poolType);
            return storage;
        }

        _logger.LogInformation("Requested {PoolType} pool not in cache, checking availability", poolType);

        // For Installation pool: check if it has become available since construction
        // This handles the case where InstallationPoolRootPath is set after CasPoolManager was created
        if (poolType == CasPoolType.Installation)
        {
            var isAvailable = _poolResolver.IsInstallationPoolAvailable();
            _logger.LogInformation("Installation pool availability check: {IsAvailable}", isAvailable);

            if (isAvailable)
            {
                _logger.LogInformation("Installation pool has become available, initializing now");
                InitializePool(CasPoolType.Installation);

                // Try to get it again after initialization
                if (_storages.TryGetValue(poolType, out storage))
                {
                    return storage;
                }
            }
            else
            {
                _logger.LogWarning("Installation pool requested but not available, falling back to primary pool");
                return _storages[CasPoolType.Primary];
            }
        }

        // Initialize the pool on-demand if not already initialized
        _logger.LogInformation("Initializing {PoolType} pool on-demand", poolType);
        InitializePool(poolType);
        return _storages[poolType];
    }

    /// <inheritdoc/>
    public ICasStorage GetStorage(ContentType contentType)
    {
        var poolType = _poolResolver.ResolvePool(contentType);
        return GetStorage(poolType);
    }

    /// <inheritdoc/>
    public IReadOnlyList<ICasStorage> GetAllStorages()
    {
        var storages = _storages.Values.ToList();
        var legacyInstallationStorage = _legacyInstallationStorage;
        if (legacyInstallationStorage != null && !storages.Contains(legacyInstallationStorage))
        {
            storages.Add(legacyInstallationStorage);
        }

        return storages.AsReadOnly();
    }

    /// <summary>
    /// Ensures both primary and installation pools are initialized and ready to use.
    /// This method should be called before operations that might span both pools.
    /// </summary>
    public void EnsureAllPoolsInitialized()
    {
        _logger.LogDebug("Ensuring all CAS pools are initialized");

        // Always ensure Primary pool is initialized
        if (!_storages.ContainsKey(CasPoolType.Primary))
        {
            _logger.LogInformation("Primary pool not initialized, initializing now");
            InitializePool(CasPoolType.Primary);
        }

        if (!_storages.ContainsKey(CasPoolType.Installation) && _poolResolver.IsInstallationPoolAvailable())
        {
            InitializePool(CasPoolType.Installation);
        }
    }

    /// <summary>
    /// Reinitializes the Installation CAS pool. This removes any existing Installation pool
    /// and recreates it if the pool path is available.
    /// </summary>
    public void ReinitializeInstallationPool()
    {
        _logger.LogInformation("Force reinitializing Installation CAS pool");

        _writabilityProbe.Invalidate();

        lock (_initLock)
        {
            if (_storages.TryRemove(CasPoolType.Installation, out _))
            {
                _logger.LogDebug("Removed existing Installation pool for reinitialization");
            }

            _installationPoolRoot = null;
            _legacyInstallationStorage = null;
            _legacyInstallationPoolRoot = null;
        }

        RefreshInstallationPools();
    }

    private void InitializePool(CasPoolType poolType)
    {
        // Double-check locking to ensure thread safety
        if (_storages.ContainsKey(poolType))
        {
            return;
        }

        lock (_initLock)
        {
            if (_storages.ContainsKey(poolType))
            {
                _logger.LogDebug("Pool {PoolType} already initialized (race condition prevented)", poolType);
                return;
            }

            var rootPath = _poolResolver.GetPoolRootPath(poolType);
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                _logger.LogWarning("Cannot initialize {PoolType} pool: root path is not configured", poolType);
                return;
            }

            // Security Guard: Prevent initializing CAS in the application directory or an empty path
            if (IsInsideApplicationDirectory(rootPath))
            {
                _logger.LogError("Security Block: Attempted to initialize {PoolType} CAS pool at or inside the application directory: {Path}. This is not allowed.", poolType, rootPath);
                return;
            }

            var storage = CreateStorage(rootPath);
            _storages.TryAdd(poolType, storage);

            if (poolType == CasPoolType.Installation)
            {
                _installationPoolRoot = rootPath;
            }

            _logger.LogInformation("Initialized {PoolType} CAS pool at {RootPath}", poolType, rootPath);
        }
    }

    private ICasStorage CreateStorage(string rootPath)
    {
        var poolConfig = (CasConfiguration)_config.Clone();
        poolConfig.CasRootPath = rootPath;

        return new CasStorage(
            Options.Create(poolConfig),
            _loggerFactory.CreateLogger<CasStorage>(),
            _hashProvider);
    }

    private void RefreshInstallationPools()
    {
        lock (_initLock)
        {
            var installationPoolAvailable = _poolResolver.IsInstallationPoolAvailable();
            var currentRoot = installationPoolAvailable
                ? _poolResolver.GetPoolRootPath(CasPoolType.Installation)
                : string.Empty;

            if (_storages.ContainsKey(CasPoolType.Installation) &&
                (!installationPoolAvailable ||
                 !string.Equals(currentRoot, _installationPoolRoot, PathHelper.PathComparison)))
            {
                _storages.TryRemove(CasPoolType.Installation, out _);
                _logger.LogInformation("Discarded cached installation CAS pool at {PreviousRoot}", _installationPoolRoot);
                _installationPoolRoot = null;
            }

            if (installationPoolAvailable && !_storages.ContainsKey(CasPoolType.Installation))
            {
                InitializePool(CasPoolType.Installation);
            }

            RefreshLegacyInstallationPool(currentRoot);
        }
    }

    private void RefreshLegacyInstallationPool(string activeInstallationRoot)
    {
        var legacyRoot = _poolResolver.GetLegacyInstallationPoolRootPath();
        if (string.IsNullOrWhiteSpace(legacyRoot) ||
            !Directory.Exists(legacyRoot) ||
            string.Equals(legacyRoot, activeInstallationRoot, PathHelper.PathComparison))
        {
            _legacyInstallationStorage = null;
            _legacyInstallationPoolRoot = null;
            return;
        }

        legacyRoot = Path.GetFullPath(legacyRoot);
        if (IsInsideApplicationDirectory(legacyRoot))
        {
            _legacyInstallationStorage = null;
            _legacyInstallationPoolRoot = null;
            _logger.LogError(
                "Security Block: Attempted to retain a legacy CAS pool at or inside the application directory: {Path}. This is not allowed.",
                legacyRoot);
            return;
        }

        if (string.Equals(legacyRoot, _legacyInstallationPoolRoot, PathHelper.PathComparison))
        {
            return;
        }

        _legacyInstallationStorage = CreateStorage(legacyRoot);
        _legacyInstallationPoolRoot = legacyRoot;
        _logger.LogInformation("Retaining legacy installation CAS pool {LegacyRoot} for read-only lookup", legacyRoot);
    }

    private bool IsInsideApplicationDirectory(string rootPath)
    {
        var appBaseDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var normalizedRootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

        return normalizedRootPath.Equals(appBaseDirectory, PathHelper.PathComparison) ||
            normalizedRootPath.StartsWith(
                appBaseDirectory + Path.DirectorySeparatorChar,
                PathHelper.PathComparison);
    }
}
