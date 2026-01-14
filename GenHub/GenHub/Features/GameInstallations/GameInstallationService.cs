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
IInstallationPathResolver? pathResolver = null) : IGameInstallationService, IDisposable
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
    public async Task<OperationResult<bool>> AddInstallationToCacheAsync(
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Initialize cache if not already initialized
            var initResult = await TryInitializeCacheAsync(cancellationToken);
            if (!initResult.Success && _cachedInstallations == null)
            {
                return OperationResult<bool>.CreateFailure("Failed to initialize installation cache");
            }

            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                // Convert ReadOnlyCollection to List for modification
                var installationsList = _cachedInstallations?.ToList() ?? [];

                // Check if installation already exists (by ID or path)
                var existing = installationsList.FirstOrDefault(i =>
                    i.Id == installation.Id ||
                    i.InstallationPath.Equals(installation.InstallationPath, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    logger!.LogDebug(
                        "Installation already exists in cache: {Path}",
                        installation.InstallationPath);
                    return OperationResult<bool>.CreateSuccess(true);
                }

                // Add to list
                installationsList.Add(installation);

                // Update cache with new ReadOnlyCollection
                _cachedInstallations = installationsList.AsReadOnly();

                logger!.LogInformation(
                    "Added installation to cache: {InstallationType} at {Path} (ID: {Id})",
                    installation.InstallationType,
                    installation.InstallationPath,
                    installation.Id);

                return OperationResult<bool>.CreateSuccess(true);
            }
            finally
            {
                _cacheLock.Release();
            }
        }
        catch (Exception ex)
        {
            logger!.LogError(ex, "Error adding installation to cache");
            return OperationResult<bool>.CreateFailure($"Failed to add installation to cache: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task CreateAndRegisterInstallationManifestsAsync(GameInstallation installation, CancellationToken cancellationToken = default)
    {
        logger!.LogInformation(
            "[MANIFEST-GEN] CreateAndRegisterInstallationManifestsAsync called for {InstallationType} at {Path}",
            installation.InstallationType,
            installation.InstallationPath);

        var gameDir = installation.InstallationPath;
        if (!Directory.Exists(gameDir))
        {
            logger!.LogWarning(
                "[MANIFEST-GEN] Installation directory does not exist: {Path}",
                gameDir);
            return;
        }

        logger!.LogInformation(
            "[MANIFEST-GEN] Installation check: HasGenerals={HasGenerals}, GeneralsPath={GeneralsPath}, HasZeroHour={HasZeroHour}, ZeroHourPath={ZeroHourPath}",
            installation.HasGenerals,
            installation.GeneralsPath ?? "null",
            installation.HasZeroHour,
            installation.ZeroHourPath ?? "null");

        if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath) && Directory.Exists(installation.GeneralsPath))
        {
            logger!.LogInformation(
                "[MANIFEST-GEN] Creating Generals manifest for {Path}",
                installation.GeneralsPath);

            // Select the best client for the installation manifest
            // Prioritize clients that match the installation publisher (e.g. Steam) and avoid third-party clients (GeneralsOnline)
            // so that the installation manifest version reflects the base game, not a mod/tool.
            var bestGeneralsClient = installation.AvailableGameClients
                .Where(c => c.GameType == GameType.Generals)
                .OrderByDescending(c => string.Equals(c.PublisherType, installation.InstallationType.ToIdentifierString(), StringComparison.OrdinalIgnoreCase))
                .ThenBy(c => string.Equals(c.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            await GenerateAndPoolManifestForGameTypeAsync(
                installation,
                GameType.Generals,
                installation.GeneralsPath,
                bestGeneralsClient,
                ManifestConstants.GeneralsManifestVersion,
                cancellationToken);
        }
        else
        {
            logger!.LogWarning(
                "[MANIFEST-GEN] Skipping Generals manifest: HasGenerals={HasGenerals}, PathEmpty={PathEmpty}, PathExists={PathExists}",
                installation.HasGenerals,
                string.IsNullOrEmpty(installation.GeneralsPath),
                installation.GeneralsPath != null && Directory.Exists(installation.GeneralsPath));
        }

        if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath) && Directory.Exists(installation.ZeroHourPath))
        {
            logger!.LogInformation(
                "[MANIFEST-GEN] Creating ZeroHour manifest for {Path}",
                installation.ZeroHourPath);

            // Select the best client for the installation manifest
            var bestZeroHourClient = installation.AvailableGameClients
                .Where(c => c.GameType == GameType.ZeroHour)
                .OrderByDescending(c => string.Equals(c.PublisherType, installation.InstallationType.ToIdentifierString(), StringComparison.OrdinalIgnoreCase))
                .ThenBy(c => string.Equals(c.PublisherType, PublisherTypeConstants.GeneralsOnline, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            await GenerateAndPoolManifestForGameTypeAsync(
                installation,
                GameType.ZeroHour,
                installation.ZeroHourPath,
                bestZeroHourClient,
                ManifestConstants.ZeroHourManifestVersion,
                cancellationToken);
        }
        else
        {
            logger!.LogWarning(
                "[MANIFEST-GEN] Skipping ZeroHour manifest: HasZeroHour={HasZeroHour}, PathEmpty={PathEmpty}, PathExists={PathExists}",
                installation.HasZeroHour,
                string.IsNullOrEmpty(installation.ZeroHourPath),
                installation.ZeroHourPath != null && Directory.Exists(installation.ZeroHourPath));
        }

        logger!.LogInformation(
            "[MANIFEST-GEN] Completed manifest generation for {InstallationType}",
            installation.InstallationType);
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
    /// Extracts the installation type from a manifest ID.
    /// </summary>
    /// <param name="manifestId">The manifest ID.</param>
    /// <returns>The installation type.</returns>
    private static GameInstallationType ExtractInstallationTypeFromManifestId(ManifestId manifestId)
    {
        var idString = manifestId.Value.ToLowerInvariant();

        if (idString.Contains(".steam."))
            return GameInstallationType.Steam;
        if (idString.Contains(".eaapp."))
            return GameInstallationType.EaApp;
        if (idString.Contains(".retail."))
            return GameInstallationType.Retail;
        if (idString.Contains(".thefirstdecade."))
            return GameInstallationType.TheFirstDecade;
        if (idString.Contains(".cdiso."))
            return GameInstallationType.CDISO;
        if (idString.Contains(".wine."))
            return GameInstallationType.Wine;
        if (idString.Contains(".lutris."))
            return GameInstallationType.Lutris;

        return GameInstallationType.Unknown;
    }

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
                // Generate the expected GameClient ID for this client
                // This MUST match what GameClientDetector generates (schema.version.publisher.gameclient.game)
                var installType = installation.InstallationType.ToIdentifierString();
                var normalizedVersion = ParseVersionStringToInt(matchingManifest.Version);
                var clientId = ManifestIdGenerator.GeneratePublisherContentId(
                    installType,
                    ContentType.GameClient,
                    gameType == GameType.ZeroHour ? "zerohour" : "generals",
                    normalizedVersion);

                // Create a game client from the manifest
                var gameClient = new GameClient
                {
                    Id = clientId, // Use the proper gameclient ID format
                    Name = matchingManifest.Name,
                    WorkingDirectory = gamePath,
                    GameType = gameType,
                    InstallationId = installation.Id,
                    Version = matchingManifest.Version,
                };

                logger!.LogInformation(
                    "Loaded {GameType} client from existing manifest {ManifestId} using ClientId {ClientId} (version {Version})",
                    gameType,
                    matchingManifest.Id,
                    clientId,
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

        // Store the installation path in metadata for persistence across sessions
        manifest.Metadata.SourcePath = installation.InstallationPath;

        var addResult = await contentManifestPool!.AddManifestAsync(
            manifest, gamePath, null, cancellationToken);

        if (addResult.Success)
        {
            if (gameClient != null)
            {
                // Fix: Always use the gameclient formatted ID for the GameClient object
                // to match what the wizard and profile system expect.
                var installType = installation.InstallationType.ToIdentifierString();
                var normalizedVersion = ParseVersionStringToInt(versionForManifest);
                gameClient.Id = ManifestIdGenerator.GeneratePublisherContentId(
                    installType,
                    ContentType.GameClient,
                    gameType == GameType.ZeroHour ? "zerohour" : "generals",
                    normalizedVersion);
            }

            logger!.LogInformation(
                "Pooled GameInstallation manifest {Id} for {InstallationId} ({GameType})",
                manifestId,
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
    /// Loads game installations from persisted GameInstallation manifests.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A list of installations reconstructed from manifests.</returns>
    private async Task<List<GameInstallation>> LoadInstallationsFromManifestsAsync(CancellationToken cancellationToken)
    {
        if (contentManifestPool == null)
        {
            return [];
        }

        var installations = new List<GameInstallation>();

        try
        {
            // Search for all GameInstallation manifests
            var searchQuery = new ContentSearchQuery
            {
                ContentType = ContentType.GameInstallation,
                Take = 1000, // Get all GameInstallation manifests
            };

            var searchResult = await contentManifestPool.SearchManifestsAsync(searchQuery, cancellationToken);
            if (!searchResult.Success || searchResult.Data == null || !searchResult.Data.Any())
            {
                logger!.LogDebug("No GameInstallation manifests found in pool");
                return [];
            }

            logger!.LogInformation(
                "Found {Count} GameInstallation manifests, reconstructing installations",
                searchResult.Data.Count());

            // Group manifests by installation path (multiple manifests per installation: Generals + Zero Hour)
            var manifestsByPath = searchResult.Data
                .Where(m =>
                {
                    var hasPath = !string.IsNullOrEmpty(m.Metadata.SourcePath);
                    if (!hasPath)
                    {
                        logger!.LogDebug("Skipping manifest {Id} - no SourcePath in metadata", m.Id);
                    }

                    return hasPath;
                })
                .GroupBy(m => m.Metadata.SourcePath!, StringComparer.OrdinalIgnoreCase);

            foreach (var group in manifestsByPath)
            {
                var sourcePath = group.Key;

                // Determine installation type from the first manifest ID
                var firstManifest = group.First();
                var installationType = ExtractInstallationTypeFromManifestId(firstManifest.Id);

                // Create GameInstallation object
                var installation = new GameInstallation(sourcePath, installationType)
                {
                    Id = Guid.NewGuid().ToString(), // Generate new ID
                    DetectedAt = DateTime.UtcNow,
                };

                // Populate Generals/ZeroHour paths
                installation.Fetch();

                installations.Add(installation);

                logger!.LogInformation(
                    "Reconstructed {InstallationType} installation from manifests: {Path}",
                    installationType,
                    sourcePath);
            }

            logger!.LogInformation(
                "Loaded {Count} installations from {ManifestCount} manifests",
                installations.Count,
                searchResult.Data.Count());
        }
        catch (Exception ex)
        {
            logger!.LogError(
                ex,
                "Error loading installations from manifests");
        }

        return installations;
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
            List<GameInstallation> installations;
            bool detectionHadError = !detectionResult.Success;
            if (detectionResult.Success)
            {
                installations = [.. detectionResult.Items];
                logger.LogInformation(
                    "[DIAGNOSTIC] Auto-detection found {Count} installations",
                    installations.Count);
            }
            else
            {
                logger.LogWarning(
                    "[DIAGNOSTIC] Auto-detection failed: {Errors}. Starting with empty list.",
                    string.Join(", ", detectionResult.Errors));
                installations = [];
            }

            // Load installations from persisted manifests
            var manifestInstallations = await LoadInstallationsFromManifestsAsync(cancellationToken);
            if (manifestInstallations.Count > 0)
            {
                logger.LogInformation(
                    "[DIAGNOSTIC] Loaded {Count} installations from manifests",
                    manifestInstallations.Count);

                // Merge manifest installations, avoiding duplicates by path
                foreach (var manifestInstall in manifestInstallations)
                {
                    var existingByPath = installations.FirstOrDefault(i =>
                        i.InstallationPath.Equals(manifestInstall.InstallationPath, StringComparison.OrdinalIgnoreCase));

                    if (existingByPath == null)
                    {
                        installations.Add(manifestInstall);
                        logger.LogInformation(
                            "[DIAGNOSTIC] Merged manifest installation into cache: {Path}",
                            manifestInstall.InstallationPath);
                    }
                    else
                    {
                        logger.LogDebug(
                            "[DIAGNOSTIC] Skipping manifest installation - path already exists from auto-detection: {Path}",
                            manifestInstall.InstallationPath);
                    }
                }
            }

            // Validate and resolve paths for all installations
            if (pathResolver != null && installations.Count > 0)
            {
                var validInstallations = new List<GameInstallation>();
                var resolvedCount = 0;

                foreach (var installation in installations)
                {
                    var validationResult = await pathResolver.ValidateInstallationPathAsync(installation, cancellationToken);
                    if (validationResult.Success && validationResult.Data)
                    {
                        // Path is valid, keep as-is
                        validInstallations.Add(installation);
                    }
                    else
                    {
                        // Path is invalid, try to resolve
                        logger.LogWarning(
                            "Installation path is invalid: {Path}. Attempting to resolve...",
                            installation.InstallationPath);

                        var resolveResult = await pathResolver.ResolveInstallationPathAsync(installation, cancellationToken);
                        if (resolveResult.Success && resolveResult.Data != null)
                        {
                            validInstallations.Add(resolveResult.Data);
                            resolvedCount++;
                            logger.LogInformation(
                                "Successfully resolved installation path from {OldPath} to {NewPath}",
                                installation.InstallationPath,
                                resolveResult.Data.InstallationPath);
                        }
                        else
                        {
                            logger.LogWarning(
                                "Could not resolve installation path for {Id}, removing from cache",
                                installation.Id);
                        }
                    }
                }

                installations = validInstallations;
                if (resolvedCount > 0)
                {
                    logger.LogInformation(
                        "Resolved {ResolvedCount} installation paths",
                        resolvedCount);
                }
            }

            // Generate manifests and populate AvailableVersions for each installation
            await PopulateGameClientsAndManifestsAsync(installations, cancellationToken);

            _cachedInstallations = installations.AsReadOnly();

            logger.LogInformation(
                    "[DIAGNOSTIC] Cache initialized with {Count} total installations",
                    _cachedInstallations.Count);

            // Return failure if detection had an error and we have no installations
            if (detectionHadError && installations.Count == 0)
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
                await CreateAndRegisterInstallationManifestsAsync(installation, cancellationToken);
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
