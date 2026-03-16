using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private readonly CasConfiguration _config;
    private readonly ConcurrentDictionary<CasPoolType, ICasStorage> _storages = new();
    private readonly object _initLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="CasPoolManager"/> class.
    /// </summary>
    /// <param name="poolResolver">The pool resolver for routing decisions.</param>
    /// <param name="config">The CAS configuration.</param>
    /// <param name="hashProvider">The file hash provider.</param>
    /// <param name="loggerFactory">The logger factory for creating storage loggers.</param>
    /// <param name="logger">The logger instance.</param>
    public CasPoolManager(
        ICasPoolResolver poolResolver,
        IOptions<CasConfiguration> config,
        IFileHashProvider hashProvider,
        ILoggerFactory loggerFactory,
        ILogger<CasPoolManager> logger)
    {
        _poolResolver = poolResolver;
        _config = config.Value;
        _hashProvider = hashProvider;
        _loggerFactory = loggerFactory;
        _logger = logger;

        // Initialize primary pool
        InitializePool(CasPoolType.Primary);

        // Initialize installation pool if configured
        if (_poolResolver.IsInstallationPoolAvailable())
        {
            InitializePool(CasPoolType.Installation);
        }
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
        return _storages.Values.ToList().AsReadOnly();
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

        // Try to initialize Installation pool if available
        if (!_storages.ContainsKey(CasPoolType.Installation) && _poolResolver.IsInstallationPoolAvailable())
        {
            _logger.LogInformation("Installation pool not initialized but is available, initializing now");
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

        // Remove existing Installation pool if present
        if (_storages.TryRemove(CasPoolType.Installation, out _))
        {
            _logger.LogDebug("Removed existing Installation pool for reinitialization");
        }

        // Reinitialize if path is available
        if (_poolResolver.IsInstallationPoolAvailable())
        {
            InitializePool(CasPoolType.Installation);
        }
        else
        {
            _logger.LogWarning("Installation pool path not available, cannot reinitialize");
        }
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
                var appBaseDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
                var normalizedRootPath = Path.TrimEndingDirectorySeparator(rootPath);

                if (normalizedRootPath.Equals(appBaseDir, StringComparison.OrdinalIgnoreCase) ||
                    normalizedRootPath.StartsWith(appBaseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("Security Block: Attempted to initialize {PoolType} CAS pool at or inside the application directory: {Path}. This is not allowed.", poolType, rootPath);
                    return;
                }

                // Create a configuration specific to this pool
                var poolConfig = new CasConfiguration
                {
                    CasRootPath = rootPath,
                    HashAlgorithm = _config.HashAlgorithm,
                    GcGracePeriod = _config.GcGracePeriod,
                    MaxCacheSizeBytes = _config.MaxCacheSizeBytes,
                    AutoGcInterval = _config.AutoGcInterval,
                    MaxConcurrentOperations = _config.MaxConcurrentOperations,
                    VerifyIntegrity = _config.VerifyIntegrity,
                    EnableAutomaticGc = _config.EnableAutomaticGc,
                };

                var poolConfigOptions = Options.Create(poolConfig);
                var storageLogger = _loggerFactory.CreateLogger<CasStorage>();

                var storage = new CasStorage(poolConfigOptions, storageLogger, _hashProvider);
                _storages.TryAdd(poolType, storage);

                _logger.LogInformation("Initialized {PoolType} CAS pool at {RootPath}", poolType, rootPath);
            }
    }
}
