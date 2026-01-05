using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
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
    public void InvalidateCache()
    {
        _cacheLock.Wait();
        try
        {
            _cachedInstallations = null;
            logger!.LogInformation("Installation cache invalidated");
        }
        finally
        {
            _cacheLock.Release();
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
    /// Parses a version string into an integer for manifest ID generation.
    /// Examples: "1.08" -> 108, "1.04" -> 104, "2.0" -> 200, "5" -> 5, null -> 0.
    /// </summary>
    /// <param name="version">The version string to parse.</param>
    /// <returns>The parsed integer version.</returns>
    private static int ParseVersionStringToInt(string? version) => GameVersionHelper.NormalizeVersion(version);

    /// <summary>
    /// Attempts to load game clients from existing manifests in the pool.
    /// </summary>
    /// <param name="installations">The installations to populate.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of installations that need full detection (don't have manifests).</returns>
    private async Task<List<GameInstallation>> TryLoadGameClientsFromManifestsAsync(
        List<GameInstallation> installations,
        CancellationToken cancellationToken)
    {
        var installationsNeedingDetection = new List<GameInstallation>();

        foreach (var installation in installations)
        {
            var clients = new List<GameClient>();
            var needsDetection = false;

            // Try to load Generals client from manifest
            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                var generalsClient = await TryLoadGameClientFromManifestAsync(
                    installation,
                    GameType.Generals,
                    installation.GeneralsPath,
                    cancellationToken);

                if (generalsClient != null)
                {
                    clients.Add(generalsClient);
                    logger!.LogDebug(
                        "Loaded Generals client from manifest for installation {Id}",
                        installation.Id);
                }
                else
                {
                    needsDetection = true;
                    logger!.LogDebug(
                        "No manifest found for Generals in installation {Id} - will trigger detection",
                        installation.Id);
                }
            }

            // Try to load Zero Hour client from manifest
            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                var zeroHourClient = await TryLoadGameClientFromManifestAsync(
                    installation,
                    GameType.ZeroHour,
                    installation.ZeroHourPath,
                    cancellationToken);

                if (zeroHourClient != null)
                {
                    clients.Add(zeroHourClient);
                    logger!.LogDebug(
                        "Loaded ZeroHour client from manifest for installation {Id}",
                        installation.Id);
                }
                else
                {
                    needsDetection = true;
                    logger!.LogDebug(
                        "No manifest found for ZeroHour in installation {Id} - will trigger detection",
                        installation.Id);
                }
            }

            if (clients.Count > 0)
            {
                installation.PopulateGameClients(clients);
                logger!.LogInformation(
                    "Populated {Count} clients from manifests for installation {Id}",
                    clients.Count,
                    installation.Id);
            }

            // Only add to detection list if this installation needs it
            if (needsDetection)
            {
                installationsNeedingDetection.Add(installation);
            }
        }

        if (installationsNeedingDetection.Count > 0)
        {
            logger!.LogInformation(
                "{NeedDetectionCount} of {TotalCount} installations need game client detection",
                installationsNeedingDetection.Count,
                installations.Count);
        }
        else
        {
            logger!.LogInformation(
                "All {TotalCount} installations loaded from existing manifests - no detection needed",
                installations.Count);
        }

        return installationsNeedingDetection;
    }

    /// <summary>
    /// Attempts to load a game client from an existing manifest.
    /// </summary>
    /// <param name="installation">The installation.</param>
    /// <param name="gameType">The game type.</param>
    /// <param name="gamePath">The game path.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The game client if manifest exists, null otherwise.</returns>
    private async Task<GameClient?> TryLoadGameClientFromManifestAsync(
        GameInstallation installation,
        GameType gameType,
        string gamePath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Search for ANY GameInstallation manifest matching this installation type and game type
            // This avoids hardcoded version candidates and works regardless of version number
            var searchQuery = new ContentSearchQuery
            {
                ContentType = ContentType.GameInstallation,
                TargetGame = gameType,
                Take = 100, // Get all matching manifests
            };

            var searchResult = await contentManifestPool!.SearchManifestsAsync(searchQuery, cancellationToken);

            if (!searchResult.Success || searchResult.Data == null)
            {
                logger!.LogDebug(
                    "No manifests found when searching for {GameType} in installation {Id}",
                    gameType,
                    installation.Id);
                return null;
            }

            // Filter results to match the installation type and ensure ID matches version
            var installTypeString = installation.InstallationType.ToIdentifierString();
            var gameTypeString = gameType == GameType.ZeroHour ? "zerohour" : "generals";

            var matchingManifest = searchResult.Data
                .Where(m => m.Id.Value.Contains($".{installTypeString}.gameinstallation.{gameTypeString}"))
                .OrderByDescending(m => m.Version) // Prefer higher versions
                .FirstOrDefault(m =>
                {
                    // Verify that the manifest ID is consistent with its version
                    // This prevents using a "version 0" manifest for a specific version (e.g. 1.04)
                    var expectedId = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, m.Version);
                    return string.Equals(m.Id.Value, expectedId, StringComparison.OrdinalIgnoreCase);
                });

            if (matchingManifest != null)
            {
                // Create a game client from the manifest
                var gameClient = new GameClient
                {
                    Id = matchingManifest.Id.Value,
                    Name = matchingManifest.Name,
                    WorkingDirectory = gamePath,
                    GameType = gameType,
                    InstallationId = installation.Id,
                    Version = matchingManifest.Version,
                };

                logger!.LogInformation(
                    "Loaded {GameType} client from existing manifest {ManifestId} (version {Version}) - skipping directory scan",
                    gameType,
                    matchingManifest.Id,
                    matchingManifest.Version);

                return gameClient;
            }

            logger!.LogDebug(
                "No existing manifest found for {InstallType} {GameType} in installation {Id}",
                installTypeString,
                gameType,
                installation.Id);

            return null;
        }
        catch (Exception ex)
        {
            logger!.LogWarning(
                ex,
                "Error loading game client from manifest for {GameType} in installation {Id}",
                gameType,
                installation.Id);
            return null;
        }
    }

    /// <summary>
    /// Generates and pools a content manifest for a specific game type within an installation.
    /// </summary>
    /// <param name="installation">The game installation.</param>
    /// <param name="gameType">The type of game (Generals or ZeroHour).</param>
    /// <param name="gamePath">The path to the game directory.</param>
    /// <param name="gameClient">The detected game client, if available.</param>
    /// <param name="defaultManifestVersion">The default manifest version if detection fails.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    private async Task GenerateAndPoolManifestForGameTypeAsync(
        GameInstallation installation,
        GameType gameType,
        string gamePath,
        GameClient? gameClient,
        string defaultManifestVersion,
        CancellationToken cancellationToken)
    {
        var detectedVersion = gameClient?.Version;
        int versionForId;
        string versionForManifest;

        if (string.IsNullOrEmpty(detectedVersion) ||
            detectedVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            // If version is unknown, use the default version for the game type (1.04/1.08)
            // This ensures we match the ID generated during dependency resolution
            versionForManifest = defaultManifestVersion;
            versionForId = ParseVersionStringToInt(versionForManifest);
        }
        else
        {
            versionForId = ParseVersionStringToInt(detectedVersion);
            versionForManifest = detectedVersion;
        }

        var idResult = ManifestIdGenerator.GenerateGameInstallationId(
            installation, gameType, versionForId);
        var manifestId = ManifestId.Create(idResult);

        var existingManifest = await contentManifestPool!.GetManifestAsync(
            manifestId, cancellationToken);

        if (existingManifest.Success && existingManifest.Data != null)
        {
            logger!.LogDebug(
                "Manifest {Id} already exists in pool, skipping generation",
                manifestId);
            return;
        }

        var manifestBuilder = await manifestGenerationService!
            .CreateGameInstallationManifestAsync(
                gamePath,
                gameType,
                installation.InstallationType,
                versionForManifest);

        var manifest = manifestBuilder.Build();
        manifest.ContentType = ContentType.GameInstallation;
        manifest.Id = manifestId;

        var addResult = await contentManifestPool!.AddManifestAsync(
            manifest, gamePath, null, cancellationToken);

        if (addResult.Success)
        {
            if (gameClient != null)
            {
                gameClient.Id = manifestId.Value;
            }

            logger!.LogInformation(
                "Pooled GameInstallation manifest {Id} for {InstallationId} ({GameType})",
                manifest.Id,
                installation.Id,
                gameType);
        }
        else
        {
            logger!.LogWarning(
                "Failed to pool {GameType} GameInstallation manifest for {InstallationId}: {Errors}",
                gameType,
                installation.Id,
                string.Join(", ", addResult.Errors));
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

            var cachedList = _cachedInstallations.ToList();
            await PopulateGameClientsAndManifestsAsync(cachedList, cancellationToken);

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
                mergedInstallations = [.. detectionResult.Items];
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
                mergedInstallations = [];
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
    /// Populates the AvailableGameClients for a detected installation by generating content manifests.
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

        if (manifestGenerationService == null || contentManifestPool == null)
        {
            logger!.LogDebug("Manifest generation skipped: services not available");
            return;
        }

        // Load game clients from existing manifests to avoid expensive directory scanning
        // Returns only installations that don't have manifests and need detection
        var installationsNeedingDetection = await TryLoadGameClientsFromManifestsAsync(installations, cancellationToken);

        if (installationsNeedingDetection.Count > 0)
        {
            // Run game client detection ONLY for installations without manifests
            logger!.LogInformation(
                "Running game client detection for {Count} installations (out of {Total} total)",
                installationsNeedingDetection.Count,
                installations.Count);

            var clientResult = await clientOrchestrator.DetectGameClientsFromInstallationsAsync(
                installationsNeedingDetection,
                cancellationToken);

            if (!clientResult.Success)
            {
                logger!.LogWarning("Client detection failed: {Errors}", string.Join(", ", clientResult.Errors));
                return;
            }

            var clientsByInstallation = clientResult.Items.GroupBy(v => v.InstallationId);
            foreach (var installation in installationsNeedingDetection)
            {
                var installationClients = clientsByInstallation.FirstOrDefault(g => g.Key == installation.Id)?.ToList() ?? Enumerable.Empty<GameClient>();
                installation.PopulateGameClients(installationClients);
                logger!.LogInformation(
                    "Populated {ClientCount} clients for installation {Id} via detection",
                    installationClients.Count(),
                    installation.Id);
            }

            // Generate GameInstallation manifests for all installations
            // This ensures base installation manifests exist for profile dependency resolution
            foreach (var installation in installations)
            {
                var gameDir = installation.InstallationPath;
                if (!Directory.Exists(gameDir)) continue;

                if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath) && Directory.Exists(installation.GeneralsPath))
                {
                    await GenerateAndPoolManifestForGameTypeAsync(
                        installation,
                        GameType.Generals,
                        installation.GeneralsPath,
                        installation.GeneralsClient,
                        ManifestConstants.GeneralsManifestVersion,
                        cancellationToken);
                }

                if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath) && Directory.Exists(installation.ZeroHourPath))
                {
                    await GenerateAndPoolManifestForGameTypeAsync(
                        installation,
                        GameType.ZeroHour,
                        installation.ZeroHourPath,
                        installation.ZeroHourClient,
                        ManifestConstants.ZeroHourManifestVersion,
                        cancellationToken);
                }
            }
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
