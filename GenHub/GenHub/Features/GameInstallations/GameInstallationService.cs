using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameInstallations;

/// <summary>
/// Provides services for managing game installations with content manifest generation.
/// </summary>
/// <remarks>
/// Integrates with <see cref="IManifestGenerationService"/> to automatically generate
/// content manifests for detected installations and populate their AvailableClients.
/// </remarks>
public class GameInstallationService(
IGameInstallationDetectionOrchestrator detectionOrchestrator,
IGameClientDetectionOrchestrator clientOrchestrator,
ILogger<GameInstallationService> logger,
IManifestGenerationService? manifestGenerationService = null,
IContentManifestPool? contentManifestPool = null,
IManualInstallationStorage? manualInstallationStorage = null) : IGameInstallationService, IDisposable
{
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private ReadOnlyCollection<GameInstallation>? _cachedInstallations;
    private bool _disposed = false;

    /// <summary>
    /// Gets a game installation by its ID.
    /// </summary>
    /// <param name="installationId">The installation ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An <see cref="OperationResult{GameInstallation}"/> containing the installation or error.</returns>
    public async Task<OperationResult<GameInstallation>> GetInstallationAsync(string installationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(installationId))
        {
            return OperationResult<GameInstallation>.CreateFailure("Installation ID cannot be null or empty.");
        }

        var initResult = await TryInitializeCacheAsync(cancellationToken);
        if (!initResult.Success)
        {
            return OperationResult<GameInstallation>.CreateFailure(initResult.Errors[0]);
        }

        if (_cachedInstallations == null)
        {
            return OperationResult<GameInstallation>.CreateFailure("Failed to detect game installations.");
        }

        var installation = _cachedInstallations.FirstOrDefault(i => i.Id == installationId);

        if (installation == null)
        {
            return OperationResult<GameInstallation>.CreateFailure($"Game installation with ID '{installationId}' not found.");
        }

        return OperationResult<GameInstallation>.CreateSuccess(installation);
    }

    /// <summary>
    /// Gets all available game installations.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing all installations or error.</returns>
    public async Task<OperationResult<IReadOnlyList<GameInstallation>>> GetAllInstallationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var initResult = await TryInitializeCacheAsync(cancellationToken);
            if (!initResult.Success)
            {
                // If TryInitializeCacheAsync failed and cache is null, return failure
                if (_cachedInstallations == null)
                {
                    return OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure(initResult.Errors[0]);
                }
            }

            if (_cachedInstallations == null)
            {
                return OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure("Failed to detect game installations.");
            }

            return OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(_cachedInstallations);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure($"Error retrieving installations: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> RegisterManualInstallationAsync(GameInstallation installation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);

        logger!.LogInformation(
            "[DIAGNOSTIC] RegisterManualInstallationAsync called for installation ID: {InstallationId}, Path: {Path}, Type: {Type}",
            installation.Id,
            installation.InstallationPath,
            installation.InstallationType);

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Ensure cache is initialized (or at least exists)
            if (_cachedInstallations == null)
            {
                logger.LogWarning(
                    "[DIAGNOSTIC] Cache is null during RegisterManualInstallationAsync, initializing with auto-detection only");

                // Try to initialize standard way first
                try
                {
                    // logic from TryInitializeCacheAsync but without the lock since we hold it
                    var detectionResult = await detectionOrchestrator.DetectAllInstallationsAsync(cancellationToken);
                    if (detectionResult.Success)
                    {
                        var installations = detectionResult.Items.ToList();
                        await PopulateGameClientsAndManifestsAsync(installations, cancellationToken);
                        _cachedInstallations = installations.AsReadOnly();
                        logger.LogInformation(
                            "[DIAGNOSTIC] Cache initialized with {Count} auto-detected installations",
                            _cachedInstallations.Count);
                    }
                    else
                    {
                        // Fallback to empty list
                        _cachedInstallations = new List<GameInstallation>().AsReadOnly();
                        logger.LogWarning(
                            "[DIAGNOSTIC] Auto-detection failed, cache initialized as empty list");
                    }
                }
                catch
                {
                     _cachedInstallations = new List<GameInstallation>().AsReadOnly();
                     logger.LogWarning(
                        "[DIAGNOSTIC] Exception during cache initialization, cache initialized as empty list");
                }
            }

            var currentList = _cachedInstallations.ToList();
            var previousCount = currentList.Count;

            // Update existing or add new
            var existingIndex = currentList.FindIndex(i => i.Id == installation.Id);
            if (existingIndex >= 0)
            {
                currentList[existingIndex] = installation;
                logger.LogInformation(
                    "[DIAGNOSTIC] Updated existing manual installation: {Id} ({Path})",
                    installation.Id,
                    installation.InstallationPath);
            }
            else
            {
                currentList.Add(installation);
                logger.LogInformation(
                    "[DIAGNOSTIC] Added new manual installation: {Id} ({Path})",
                    installation.Id,
                    installation.InstallationPath);
            }

            _cachedInstallations = currentList.AsReadOnly();

            logger.LogInformation(
                "[DIAGNOSTIC] Cache now contains {Count} installations (was {PreviousCount})",
                _cachedInstallations.Count,
                previousCount);

            // Persist manual installation to storage
            if (manualInstallationStorage != null)
            {
                try
                {
                    await manualInstallationStorage.SaveManualInstallationAsync(installation, cancellationToken);
                    logger.LogInformation(
                        "[DIAGNOSTIC] Persisted manual installation {Id} to storage",
                        installation.Id);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "[DIAGNOSTIC] Failed to persist manual installation {Id} to storage",
                        installation.Id);
                }
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <inheritdoc/>
    public void InvalidateCache()
    {
        _cacheLock.Wait();
        try
        {
            var previousCount = _cachedInstallations?.Count ?? 0;
            _cachedInstallations = null;
            logger!.LogInformation(
                "[DIAGNOSTIC] Installation cache invalidated (had {PreviousCount} installations, now null)",
                previousCount);

            // Clear manual installations from storage when cache is invalidated
            if (manualInstallationStorage != null)
            {
                try
                {
                    manualInstallationStorage.ClearAllAsync(default).GetAwaiter().GetResult();
                    logger.LogInformation(
                        "[DIAGNOSTIC] Cleared manual installations from storage");
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "[DIAGNOSTIC] Failed to clear manual installations from storage");
                }
            }
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Releases resources used by the <see cref="GameInstallationService"/>.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="GameInstallationService"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _cacheLock?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Attempts to initialize the installation cache if not already initialized.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation, containing a result indicating success/failure.</returns>
    private async Task<OperationResult<bool>> TryInitializeCacheAsync(CancellationToken cancellationToken)
    {
        if (_cachedInstallations != null)
        {
            logger!.LogInformation(
                "[DIAGNOSTIC] TryInitializeCacheAsync: Cache already initialized with {Count} installations",
                _cachedInstallations.Count);
            return OperationResult<bool>.CreateSuccess(true);
        }

        logger!.LogInformation(
            "[DIAGNOSTIC] TryInitializeCacheAsync: Cache is null, initializing via auto-detection and manual installations");

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedInstallations != null)
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            var detectionResult = await detectionOrchestrator.DetectAllInstallationsAsync(cancellationToken);

            // Start with auto-detected installations if successful, otherwise empty list
            List<GameInstallation> mergedInstallations;
            int autoCount = 0;
            bool detectionHadError = !detectionResult.Success;
            if (detectionResult.Success)
            {
                mergedInstallations = detectionResult.Items.ToList();
                autoCount = mergedInstallations.Count;
                logger.LogInformation(
                    "[DIAGNOSTIC] Auto-detection found {Count} installations",
                    autoCount);
            }
            else
            {
                logger.LogWarning(
                    "[DIAGNOSTIC] Auto-detection failed: {Errors}. Starting with empty list.",
                    string.Join(", ", detectionResult.Errors));
                mergedInstallations = new List<GameInstallation>();
            }

            // Load persisted manual installations and merge with auto-detected ones
            if (manualInstallationStorage != null)
            {
                try
                {
                    var manualInstallations = await manualInstallationStorage.LoadManualInstallationsAsync(cancellationToken);
                    var loadedManualCount = manualInstallations.Count;

                    // Merge manual installations, avoiding duplicates by path
                    foreach (var manualInstall in manualInstallations)
                    {
                        var existingByPath = mergedInstallations.FirstOrDefault(i =>
                            i.InstallationPath.Equals(manualInstall.InstallationPath, StringComparison.OrdinalIgnoreCase));

                        if (existingByPath != null)
                        {
                            logger.LogInformation(
                                    "[DIAGNOSTIC] Skipping manual installation {Id} - path conflicts with auto-detected installation",
                                    manualInstall.Id);
                        }
                        else
                        {
                            mergedInstallations.Add(manualInstall);
                            logger.LogInformation(
                                    "[DIAGNOSTIC] Merged manual installation {Id} into cache",
                                    manualInstall.Id);
                        }
                    }

                    logger.LogInformation(
                            "[DIAGNOSTIC] Loaded {ManualCount} manual installations from storage",
                            loadedManualCount);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                            ex,
                            "[DIAGNOSTIC] Failed to load manual installations from storage");
                }
            }

            // Generate manifests and populate AvailableVersions for each installation
            await PopulateGameClientsAndManifestsAsync(mergedInstallations, cancellationToken);

            _cachedInstallations = mergedInstallations.AsReadOnly();

            var totalCount = _cachedInstallations.Count;
            var manualCount = totalCount - autoCount;

            logger.LogInformation(
                    "[DIAGNOSTIC] Cache initialized with {Count} total installations ({AutoCount} auto-detected, {ManualCount} manual)",
                    totalCount,
                    autoCount,
                    manualCount);

            // Return failure if detection had an error and we have no installations
            if (detectionHadError && mergedInstallations.Count == 0)
            {
                return OperationResult<bool>.CreateFailure(
                    $"Failed to detect game installations: {string.Join(", ", detectionResult.Errors)}");
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Populates the AvailableGameClients for a detected installation.
    /// </summary>
    /// <param name="installations">The installations to populate clients for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task PopulateGameClientsAndManifestsAsync(List<GameInstallation> installations, CancellationToken cancellationToken)
    {
        foreach (var installation in installations)
        {
            installation.Fetch();
        }

        var clientResult = await clientOrchestrator.DetectGameClientsFromInstallationsAsync(installations, cancellationToken);
        if (!clientResult.Success)
        {
            logger!.LogWarning("Client detection failed: {Errors}", string.Join(", ", clientResult.Errors));
            return;
        }

        var clientsByInstallation = clientResult.Items.GroupBy(v => v.InstallationId);
        foreach (var installation in installations)
        {
            var installationClients = clientsByInstallation.FirstOrDefault(g => g.Key == installation.Id)?.ToList() ?? Enumerable.Empty<GameClient>();
            installation.PopulateGameClients(installationClients);
            logger!.LogDebug("Populated {ClientCount} clients for installation {Id}", installationClients.Count(), installation.Id);
        }

        // Create and register GameInstallation manifests to the pool
        if (manifestGenerationService != null && contentManifestPool != null)
        {
            foreach (var installation in installations)
            {
                await CreateAndRegisterInstallationManifestsAsync(installation, cancellationToken);
            }
        }
        else
        {
            logger!.LogWarning("ManifestGenerationService or ContentManifestPool not available, skipping GameInstallation manifest registration");
        }
    }

    /// <summary>
    /// Creates and registers GameInstallation manifests for an installation.
    /// </summary>
    /// <param name="installation">The installation to create manifests for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CreateAndRegisterInstallationManifestsAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        // Create manifest for Generals if it exists
        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
        {
            await CreateAndRegisterSingleInstallationManifestAsync(
                installation,
                GameType.Generals,
                installation.GeneralsPath,
                cancellationToken);
        }

        // Create manifest for Zero Hour if it exists
        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
        {
            await CreateAndRegisterSingleInstallationManifestAsync(
                installation,
                GameType.ZeroHour,
                installation.ZeroHourPath,
                cancellationToken);
        }
    }

    /// <summary>
    /// Creates and registers a single GameInstallation manifest.
    /// </summary>
    /// <param name="installation">The installation.</param>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <param name="installationPath">The path to the game installation.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task CreateAndRegisterSingleInstallationManifestAsync(
        GameInstallation installation,
        GameType gameType,
        string installationPath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Find a base game client for this game type to determine version
            var baseGameClient = installation.AvailableGameClients
                .FirstOrDefault(c => c.GameType == gameType && !c.IsPublisherClient);

            if (baseGameClient == null)
            {
                logger!.LogWarning(
                    "No base game client found for {GameType} in installation {InstallationId}, skipping GameInstallation manifest creation",
                    gameType,
                    installation.Id);
                return;
            }

            // Determine version for manifest
            var version = baseGameClient.Version;
            if (string.IsNullOrEmpty(version) ||
                version.Equals(GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase) ||
                version.Equals("Auto-Updated", StringComparison.OrdinalIgnoreCase) ||
                version.Equals(GameClientConstants.AutoDetectedVersion, StringComparison.OrdinalIgnoreCase))
            {
                version = gameType == GameType.ZeroHour
                    ? ManifestConstants.ZeroHourManifestVersion
                    : ManifestConstants.GeneralsManifestVersion;
            }

            // Create the GameInstallation manifest
            var manifestBuilder = await manifestGenerationService!.CreateGameInstallationManifestAsync(
                installationPath,
                gameType,
                installation.InstallationType,
                version);

            var manifest = manifestBuilder.Build();

            // Register the manifest to the pool
            var addResult = await contentManifestPool!.AddManifestAsync(manifest, installationPath, null, cancellationToken);

            if (addResult.Success)
            {
                logger!.LogInformation(
                    "Registered GameInstallation manifest {ManifestId} for {GameType} in installation {InstallationId}",
                    manifest.Id,
                    gameType,
                    installation.Id);
            }
            else
            {
                logger!.LogWarning(
                    "Failed to register GameInstallation manifest for {GameType} in installation {InstallationId}: {Errors}",
                    gameType,
                    installation.Id,
                    string.Join(", ", addResult.Errors));
            }
        }
        catch (Exception ex)
        {
            logger!.LogError(
                ex,
                "Error creating GameInstallation manifest for {GameType} in installation {InstallationId}",
                gameType,
                installation.Id);
        }
    }
}
