using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameClients;

/// <summary>
/// Detects game clients from installations and directories.
/// </summary>
public class GameClientDetector(
    IManifestGenerationService manifestGenerationService,
    IContentManifestPool contentManifestPool,
    IFileHashProvider hashProvider,
    IGameClientHashRegistry hashRegistry,
    IEnumerable<IGameClientIdentifier> gameClientIdentifiers,
    ILogger<GameClientDetector> logger) : IGameClientDetector
{
    // Directories to exclude from recursive scanning to avoid duplicates and performance issues
    private static readonly HashSet<string> _excludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".genhub-backup",
        ".git",
        ".vs",
        "node_modules",
        "bin",
        "obj",
        "tmp",
        "temp",
        "GeneralsOnlineGameData", // Internal data for GO client
    };

    /// <inheritdoc/>
    public async Task<DetectionResult<GameClient>> DetectGameClientsFromInstallationsAsync(
        IEnumerable<GameInstallation> installations,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var gameClients = new List<GameClient>();

        foreach (var inst in installations)
        {
            if (inst.HasGenerals && !string.IsNullOrEmpty(inst.GeneralsPath) && Directory.Exists(inst.GeneralsPath))
            {
                // First, detect the standard installation client (priority over GeneralsOnline for auto-selection)
                var (version, actualExePath) = await DetectVersionFromInstallationAsync(inst.GeneralsPath, GameType.Generals, cancellationToken);
                if (File.Exists(actualExePath))
                {
                    var generalsVersion = new GameClient
                    {
                        Name = $"Generals {version}",
                        Id = string.Empty, // Set later by manifest
                        Version = version,
                        ExecutablePath = actualExePath,
                        GameType = GameType.Generals,
                        InstallationId = inst.Id,
                        WorkingDirectory = inst.GeneralsPath,
                    };
                    await GenerateClientManifestAndSetIdAsync(generalsVersion, inst.GeneralsPath, inst, GameType.Generals);
                    gameClients.Add(generalsVersion);
                }
                else
                {
                    logger.LogWarning("Skipping Generals game client for {InstallationId}: no valid executable found at {ExePath}", inst.Id, actualExePath);
                }

                // Detect publisher clients (GeneralsOnline, SuperHackers, etc.) using registered identifiers
                var generalsPublisherClients = await DetectPublisherClientsAsync(inst, inst.GeneralsPath, GameType.Generals, cancellationToken);
                gameClients.AddRange(generalsPublisherClients);
            }

            if (inst.HasZeroHour && !string.IsNullOrEmpty(inst.ZeroHourPath) && Directory.Exists(inst.ZeroHourPath))
            {
                var (version, actualExePath) = await DetectVersionFromInstallationAsync(inst.ZeroHourPath, GameType.ZeroHour, cancellationToken);
                if (File.Exists(actualExePath))
                {
                    var zeroHourVersion = new GameClient
                    {
                        Name = $"Zero Hour {version}",
                        Id = string.Empty,
                        Version = version,
                        ExecutablePath = actualExePath,
                        GameType = GameType.ZeroHour,
                        InstallationId = inst.Id,
                        WorkingDirectory = inst.ZeroHourPath,
                    };
                    await GenerateClientManifestAndSetIdAsync(zeroHourVersion, inst.ZeroHourPath, inst, GameType.ZeroHour);
                    gameClients.Add(zeroHourVersion);
                }
                else
                {
                    logger.LogWarning("Skipping Zero Hour game client for {InstallationId}: no valid executable found at {ExePath}", inst.Id, actualExePath);
                }

                // Detect publisher clients (GeneralsOnline, SuperHackers, etc.) using registered identifiers
                var zhPublisherClients = await DetectPublisherClientsAsync(inst, inst.ZeroHourPath, GameType.ZeroHour, cancellationToken);
                gameClients.AddRange(zhPublisherClients);
            }

            // Manifest generation is now handled exclusively by GameInstallationService
            // to avoid race conditions and duplicate work during detection.
        }

        stopwatch.Stop();
        logger.LogInformation("Detected {Count} game clients from {InstallCount} installations in {ElapsedMs}ms", gameClients.Count, installations.Count(), stopwatch.ElapsedMilliseconds);

        return DetectionResult<GameClient>.CreateSuccess(gameClients, stopwatch.Elapsed);
    }

    /// <inheritdoc/>
    public async Task<DetectionResult<GameClient>> ScanDirectoryForGameClientsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        if (!Directory.Exists(path))
        {
            stopwatch.Stop();
            return DetectionResult<GameClient>.CreateFailure("Directory does not exist");
        }

        var gameClients = new List<GameClient>();

        // Search for all possible executable names using manual recursion to skip excluded directories
        var allFiles = await Task.Run(() => FindGameExecutablesRecursively(path));

        foreach (var exe in allFiles)
        {
            var dir = Path.GetDirectoryName(exe);
            if (dir != null)
            {
                // Detect the actual game client by analyzing the executable hash
                var detectedClient = await DetectGameClientFromExecutableAsync(exe, dir, cancellationToken);
                if (detectedClient != null)
                {
                    // Generate manifest and set ID
                    await GenerateClientManifestAndSetIdAsync(detectedClient, dir, null, detectedClient.GameType);
                    gameClients.Add(detectedClient);
                }
            }
        }

        stopwatch.Stop();
        logger.LogInformation("Scanned directory {Path} and found {Count} game clients in {ElapsedMs}ms", path, gameClients.Count, stopwatch.ElapsedMilliseconds);
        return DetectionResult<GameClient>.CreateSuccess(gameClients, stopwatch.Elapsed);
    }

    /// <inheritdoc/>
    public Task<bool> ValidateGameClientAsync(
        GameClient gameClient,
        CancellationToken cancellationToken = default)
    {
        var isValid = !string.IsNullOrEmpty(gameClient.ExecutablePath) && File.Exists(gameClient.ExecutablePath);
        return Task.FromResult(isValid);
    }

    /// <summary>
    /// Detects a game client from a specific executable file using hash analysis.
    /// </summary>
    /// <param name="executablePath">The path to the executable file.</param>
    /// <param name="workingDirectory">The working directory for the game client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A GameClient if detected, otherwise null.</returns>
    private async Task<GameClient?> DetectGameClientFromExecutableAsync(string executablePath, string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(executablePath))
                return null;

            var hash = await hashProvider.ComputeFileHashAsync(executablePath, cancellationToken);

            // Try to detect version for both game types
            var generalsVersion = hashRegistry.GetVersionFromHash(hash, GameType.Generals);
            var zeroHourVersion = hashRegistry.GetVersionFromHash(hash, GameType.ZeroHour);
            GameType detectedGameType;
            string detectedVersion;
            if (!string.Equals(generalsVersion, GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase))
            {
                detectedGameType = GameType.Generals;
                detectedVersion = generalsVersion;
            }
            else if (!string.Equals(zeroHourVersion, GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase))
            {
                detectedGameType = GameType.ZeroHour;
                detectedVersion = zeroHourVersion;
            }
            else
            {
                detectedGameType = GameType.Unknown;
                detectedVersion = GameClientConstants.UnknownVersion;
            }

            if (detectedGameType != GameType.Unknown && !string.Equals(detectedVersion, GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase))
            {
                var gameTypeName = detectedGameType == GameType.Generals ? "Generals" : "Zero Hour";
                logger.LogDebug("Detected {GameType} {Version} from {ExecutablePath} with hash {Hash}", gameTypeName, detectedVersion, executablePath, hash);
                return new GameClient
                {
                    Name = $"Scanned {gameTypeName} {detectedVersion} ({Path.GetFileName(workingDirectory)})",
                    Id = string.Empty, // Will be set by manifest generation
                    Version = detectedVersion,
                    ExecutablePath = executablePath,
                    GameType = detectedGameType,
                    WorkingDirectory = workingDirectory,
                    InstallationId = string.Empty,
                    SourceType = ContentType.GameClient,
                };
            }

            // If hash is not recognized, create a generic entry for manual identification
            logger.LogDebug("Unknown game executable found at {ExecutablePath} with hash {Hash}", executablePath, hash);
            return new GameClient
            {
                Name = $"Unknown Game ({Path.GetFileName(workingDirectory)})",
                Id = string.Empty, // Will be set by manifest generation
                Version = GameClientConstants.UnknownVersion,
                ExecutablePath = executablePath,
                GameType = GameType.Generals, // Default assumption
                WorkingDirectory = workingDirectory,
                InstallationId = string.Empty,
                SourceType = ContentType.GameClient,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to analyze executable {ExecutablePath}", executablePath);
            return null;
        }
    }

    private async Task GenerateClientManifestAndSetIdAsync(GameClient gameClient, string clientPath, GameInstallation? installation, GameType gameType)
    {
        try
        {
            // Validate that the GameClient has a valid executable path
            if (string.IsNullOrWhiteSpace(gameClient.ExecutablePath))
            {
                logger.LogError("GameClient {ClientName} has no executable path - cannot generate manifest", gameClient.Name);
                gameClient.Id = Guid.NewGuid().ToString(); // Fallback
                return;
            }

            if (!File.Exists(gameClient.ExecutablePath))
            {
                logger.LogError("GameClient executable not found at {ExecutablePath} - cannot generate manifest", gameClient.ExecutablePath);
                gameClient.Id = Guid.NewGuid().ToString(); // Fallback
                return;
            }

            // Determine publisher info from installation if available
            PublisherInfo? publisherInfo = null;
            if (installation != null)
            {
                var (publisherName, website, supportUrl) = PublisherInfoConstants.GetPublisherInfo(installation.InstallationType);
                publisherInfo = new PublisherInfo
                {
                    Name = publisherName,
                    Website = website,
                    SupportUrl = supportUrl,
                    PublisherType = PublisherTypeConstants.FromInstallationType(installation.InstallationType),
                };
            }

            // Generate GameClient manifest with executable included
            var builder = await manifestGenerationService.CreateGameClientManifestAsync(
                clientPath, gameType, gameClient.Name, gameClient.Version, gameClient.ExecutablePath, publisherInfo);

            var manifest = builder.Build();
            manifest.ContentType = ContentType.GameClient;

            // Add game installation dependency if installation is provided (Fix for 1.04/1.08 auto-selection)
            if (installation != null)
            {
                var dependencyName = gameType == GameType.ZeroHour
                    ? GameClientConstants.ZeroHourInstallationDependencyName
                    : GameClientConstants.GeneralsInstallationDependencyName;

                var installDependency = new ContentDependency
                {
                    Id = ManifestId.Create(ManifestConstants.DefaultContentDependencyId),
                    Name = dependencyName,
                    DependencyType = ContentType.GameInstallation,
                    InstallBehavior = DependencyInstallBehavior.RequireExisting,
                    CompatibleGameTypes = [gameType],
                    IsOptional = false,
                };
                manifest.Dependencies.Add(installDependency);
            }

            // Use ManifestIdGenerator for deterministic client ID generation
            if (installation != null)
            {
                // Use publisher-based ID generation for GameClient with correct content type
                var publisherId = installation.InstallationType.ToIdentifierString();
                var contentName = gameType == GameType.ZeroHour ? "zerohour" : "generals";

                // Convert version string to normalized integer format (e.g., "1.04" → 104, "1.08" → 108)
                int normalizedVersion = GameVersionHelper.NormalizeVersion(gameClient.Version);
                var clientIdResult = ManifestIdGenerator.GeneratePublisherContentId(publisherId, ContentType.GameClient, contentName, userVersion: normalizedVersion);
                manifest.Id = ManifestId.Create(clientIdResult);
            }
            else
            {
                // For scanned/standalone clients, generate ID based on path and hash
                var fallbackId = $"scanned-{gameType.ToString().ToLowerInvariant()}-{Path.GetFileName(clientPath)}-{gameClient.Version}";
                manifest.Id = ManifestId.Create(fallbackId);
            }

            // Add to pool
            var addResult = await contentManifestPool.AddManifestAsync(manifest, clientPath);
            if (addResult.Success)
            {
                gameClient.Id = manifest.Id.ToString();
                logger.LogInformation("Generated GameClient manifest ID {Id} for {VersionName}", gameClient.Id, gameClient.Name);
            }
            else
            {
                logger.LogWarning("Failed to pool GameClient manifest for {VersionName}: {Errors}", gameClient.Name, string.Join(", ", addResult.Errors));
                gameClient.Id = Guid.NewGuid().ToString(); // Fallback
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate manifest for {VersionName}", gameClient.Name);
            gameClient.Id = Guid.NewGuid().ToString(); // Fallback
        }
    }

    /// <summary>
    /// Detects the game version from the executable file using SHA-256 hash comparison.
    /// Supports multiple possible executable names that GenPatcher might create.
    /// </summary>
    /// <param name="installationPath">The installation directory path.</param>
    /// <param name="gameType">The type of game (Generals or ZeroHour).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing the detected version string and the actual executable path found, or (GameClientConstants.UnknownVersion, original path) if not recognized.</returns>
    private async Task<(string Version, string ExecutablePath)> DetectVersionFromInstallationAsync(string installationPath, GameType gameType, CancellationToken cancellationToken)
    {
        // Use the possible executable names from the registry
        foreach (var executableName in hashRegistry.PossibleExecutableNames)
        {
            var executablePath = Path.Combine(installationPath, executableName);
            if (!File.Exists(executablePath))
                continue;

            try
            {
                // Get the actual filename with correct casing from the filesystem
                var actualFileName = Path.GetFileName(new FileInfo(executablePath).FullName);
                var actualExecutablePath = Path.Combine(installationPath, actualFileName);

                var hash = await hashProvider.ComputeFileHashAsync(actualExecutablePath, cancellationToken);
                if (string.IsNullOrEmpty(hash))
                {
                    logger.LogWarning("Failed to compute hash for {ExecutablePath}", actualExecutablePath);
                    continue;
                }

                var version = hashRegistry.GetVersionFromHash(hash, gameType);

                if (!string.Equals(version, GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogInformation(
                        "Detected {GameType} version {Version} from {FileName} with hash {Hash}",
                        gameType,
                        version,
                        actualFileName,
                        hash);
                    return (version, actualExecutablePath);
                }
                else
                {
                    logger.LogDebug(
                        "Unknown hash for {GameType} in {ExecutableName}: {Hash}",
                        gameType,
                        actualFileName,
                        hash);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to hash file {ExecutablePath}", executablePath);
            }
        }

        // Fallback: try to detect version from generals.exe/generalszh.exe file info
        var defaultExecutableName = gameType == GameType.Generals
            ? GameClientConstants.GeneralsExecutable
            : GameClientConstants.ZeroHourExecutable;
        var defaultPath = Path.Combine(installationPath, defaultExecutableName);

        var fallbackVersion = GameClientConstants.UnknownVersion;

        if (File.Exists(defaultPath))
        {
            try
            {
                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(defaultPath);
                var rawVersion = versionInfo.ProductVersion ?? versionInfo.FileVersion;

                if (!string.IsNullOrWhiteSpace(rawVersion))
                {
                    var cleanVersion = rawVersion.Split('+')[0].Split('-')[0].Trim();
                    cleanVersion = cleanVersion.Replace(", ", ".").Replace(",", ".");
                    var components = cleanVersion.Split('.');

                    if (components.Length > 2)
                    {
                        if (components.Length >= 3 && components[0] == "1" && components[1] == "0" && components[2] != "0")
                        {
                            cleanVersion = $"1.0{components[2]}"; // 1.0.4 -> 1.04
                        }
                        else if (components.Length >= 2)
                        {
                            cleanVersion = $"{components[0]}.{components[1]}"; // 1.0.0.0 -> 1.0
                        }
                    }

                    fallbackVersion = cleanVersion;
                    logger.LogInformation(
                        "Detected {GameType} version {Version} from FileVersionInfo for {ExecutableName}",
                        gameType,
                        cleanVersion,
                        defaultExecutableName);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to read FileVersionInfo from {ExecutablePath}", defaultPath);
            }
        }

        // Apply smart defaults for generic/unknown versions
        if (fallbackVersion == GameClientConstants.UnknownVersion || fallbackVersion == "1.0" || fallbackVersion == "1.00" || fallbackVersion == "0.0" || fallbackVersion == "0.0.0.0")
        {
            var oldVersion = fallbackVersion;
            fallbackVersion = gameType == GameType.Generals ? "1.08" : "1.04";

            if (fallbackVersion != oldVersion)
            {
                logger.LogInformation(
                    "Normalized generic version '{OldVersion}' to standard latest patch '{NewVersion}' for {GameType}",
                    oldVersion,
                    fallbackVersion,
                    gameType);
            }
        }

        logger.LogInformation(
            "Using {ExecutableName} with version {Version} for {GameType}",
            defaultExecutableName,
            fallbackVersion,
            gameType);
        return (fallbackVersion, defaultPath);
    }

    /// <returns>A list of detected publisher game clients.</returns>
    private async Task<List<GameClient>> DetectPublisherClientsAsync(
        GameInstallation installation,
        string installationPath,
        GameType gameType,
        CancellationToken cancellationToken)
    {
        var detectedClients = new List<GameClient>();

        if (string.IsNullOrEmpty(installationPath) || !Directory.Exists(installationPath))
        {
            return detectedClients;
        }

        // 1. Check manifest pool for existing DOWNLOADED content from publishers detected in this path
        var detectedPublisherIds = await DetectPublisherExecutablesAsync(installationPath);
        var publishersHandledFromPool = await DetectPublisherClientsFromPoolAsync(installation, installationPath, gameType, detectedPublisherIds, detectedClients, cancellationToken);

        // 2. Special handling for GeneralsOnline (detects multiple variants)
        if (!publishersHandledFromPool.Contains(PublisherTypeConstants.GeneralsOnline))
        {
            var goClients = await DetectGeneralsOnlineClientsAsync(installation, gameType);
            if (goClients.Count > 0)
            {
                detectedClients.AddRange(goClients);
                publishersHandledFromPool.Add(PublisherTypeConstants.GeneralsOnline);
            }
        }

        // 3. Perform local detection for publishers NOT found in the pool
        await DetectPublisherClientsFromLocalFilesAsync(installation, installationPath, gameType, publishersHandledFromPool, detectedClients);

        if (detectedClients.Count > 0)
        {
            logger.LogInformation(
                "Detected {Count} publisher clients in {InstallationPath} ({FromPool} from pool, {FromLocal} from local detection)",
                detectedClients.Count,
                installationPath,
                detectedClients.Count(c => publishersHandledFromPool.Any(p => c.Id.Contains(p, StringComparison.OrdinalIgnoreCase))),
                detectedClients.Count - detectedClients.Count(c => publishersHandledFromPool.Any(p => c.Id.Contains(p, StringComparison.OrdinalIgnoreCase))));
        }

        return detectedClients;
    }

    /// <summary>
    /// Detects publisher game clients from the manifest pool for publishers found in the installation.
    /// </summary>
    private async Task<HashSet<string>> DetectPublisherClientsFromPoolAsync(
        GameInstallation installation,
        string installationPath,
        GameType gameType,
        HashSet<string> detectedPublisherIds,
        List<GameClient> detectedClients,
        CancellationToken cancellationToken)
    {
        var publishersHandledFromPool = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var publisherId in detectedPublisherIds)
        {
            var targetGameType = gameType;

            var existingManifests = await GetExistingPublisherManifestsAsync(publisherId, targetGameType, cancellationToken);

            if (existingManifests.Count > 0)
            {
                var clientsFromManifests = CreateGameClientsFromManifests(existingManifests, installation, installationPath);
                detectedClients.AddRange(clientsFromManifests);
                publishersHandledFromPool.Add(publisherId);

                logger.LogInformation(
                    "Found {Count} existing {Publisher} manifests in pool, created {ClientCount} game clients for {GameType}",
                    existingManifests.Count,
                    publisherId,
                    clientsFromManifests.Count,
                    gameType);
            }
        }

        return publishersHandledFromPool;
    }

    /// <summary>
    /// Detects publisher game clients from local files for publishers not yet handled from the pool.
    /// </summary>
    private Task DetectPublisherClientsFromLocalFilesAsync(
        GameInstallation installation,
        string installationPath,
        GameType gameType,
        HashSet<string> publishersHandledFromPool,
        List<GameClient> detectedClients)
    {
        var executableFiles = Directory.GetFiles(installationPath, "*.exe", SearchOption.TopDirectoryOnly);

        foreach (var identifier in gameClientIdentifiers)
        {
            if (publishersHandledFromPool.Contains(identifier.PublisherId))
                continue;

            foreach (var executablePath in executableFiles)
            {
                if (!identifier.CanIdentify(executablePath))
                    continue;

                try
                {
                    var identification = identifier.Identify(executablePath);
                    if (identification == null) continue;

                    // Skip if the identified game type doesn't match what we're looking for
                    if (identification.GameType != gameType && identification.GameType != GameType.Unknown)
                        continue;

                    logger.LogInformation(
                        "Detected {PublisherId} client: {DisplayName} at {ExecutablePath} (requires content acquisition)",
                        identification.PublisherId,
                        identification.DisplayName,
                        executablePath);

                    var gameClient = new GameClient
                    {
                        Name = identification.DisplayName,
                        Id = string.Empty, // No manifest ID - these are detected-only clients that should prompt for verified publisher download
                        Version = identification.LocalVersion ?? GameClientConstants.UnknownVersion,
                        ExecutablePath = executablePath,
                        GameType = gameType,
                        InstallationId = installation.Id,
                        WorkingDirectory = installationPath,
                        SourceType = ContentType.GameClient,
                        PublisherType = identification.PublisherId,
                    };

                    // Note: Manifest generation removed - user will be prompted to install verified publisher version
                    detectedClients.Add(gameClient);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to detect publisher client at {ExecutablePath}", executablePath);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Detects GeneralsOnline game clients by name in the game installation directory.
    /// This helper enables detection of GeneralsOnline executables that users already have
    /// from the existing GeneralsOnline launcher.
    /// </summary>
    /// <param name="installation">The game installation to scan.</param>
    /// <param name="gameType">The type of game (Generals or ZeroHour).</param>

    /// <returns>A list of detected GeneralsOnline game clients.</returns>
    /// <remarks>
    /// GeneralsOnline executables are auto-updated by the GeneralsOnline launcher,
    /// which can invalidate hash verification. For now, we detect by filename only
    /// and skip hash validation until a dedicated publisher system is implemented.
    /// </remarks>
    private Task<List<GameClient>> DetectGeneralsOnlineClientsAsync(
        GameInstallation installation,
        GameType gameType)
    {
        var detectedClients = new List<GameClient>();
        var installationPath = gameType == GameType.Generals ? installation.GeneralsPath : installation.ZeroHourPath;

        if (string.IsNullOrEmpty(installationPath) || !Directory.Exists(installationPath))
        {
            return Task.FromResult(detectedClients);
        }

        // GeneralsOnline clients auto-update, so we use a fixed version string
        const string generalsOnlineVersion = GameClientConstants.UnknownVersion;

        var generalsOnlineExecutables = GameClientConstants.GeneralsOnlineExecutableNames;

        foreach (var executableName in generalsOnlineExecutables)
        {
            var executablePath = Path.Combine(installationPath, executableName);

            if (!File.Exists(executablePath))
            {
                continue;
            }

            try
            {
                // Determine the variant name from the executable
                var variantName = executableName switch
                {
                    GameClientConstants.GeneralsOnline30HzExecutable => GameClientConstants.GeneralsOnline30HzDisplayName,
                    GameClientConstants.GeneralsOnline60HzExecutable => GameClientConstants.GeneralsOnline60HzDisplayName,
                    _ => null, // Skip unknown variants
                };

                // Skip if variant is not recognized
                if (variantName == null)
                {
                    logger.LogDebug(
                        "Skipping unrecognized GeneralsOnline executable: {ExecutableName}",
                        executableName);
                    continue;
                }

                logger.LogInformation(
                    "Detected GeneralsOnline client: {VariantName} at {ExecutablePath}",
                    variantName,
                    executablePath);

                // Format display name: "GeneralsOnline 60Hz"
                var displayName = $"{variantName}";

                var gameClient = new GameClient
                {
                    Name = displayName,
                    Id = string.Empty, // No manifest ID - these are detected-only clients that should prompt for verified publisher download
                    Version = generalsOnlineVersion,
                    ExecutablePath = executablePath,
                    GameType = gameType,
                    InstallationId = installation.Id,
                    WorkingDirectory = installationPath,
                    SourceType = ContentType.GameClient,
                    PublisherType = PublisherTypeConstants.GeneralsOnline,
                };

                // Note: Manifest generation removed - user will be prompted to install verified publisher version
                detectedClients.Add(gameClient);

                logger.LogDebug(
                    "Added GeneralsOnline game client {VariantName} with ID {ClientId}",
                    variantName,
                    gameClient.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to detect GeneralsOnline client at {ExecutablePath}",
                    executablePath);
            }
        }

        if (detectedClients.Count > 0)
        {
            logger.LogInformation(
                "Detected {Count} GeneralsOnline clients in {InstallationPath}",
                detectedClients.Count,
                installationPath);
        }

        return Task.FromResult(detectedClients);
    }

    /// <summary>
    /// Quickly scans installation path for publisher executables without full identification.
    /// Returns set of publisher IDs that have executables present.
    /// </summary>
    /// <param name="installationPath">The path to scan.</param>
    /// <returns>A set of publisher IDs with detected executables.</returns>
    private Task<HashSet<string>> DetectPublisherExecutablesAsync(
        string installationPath)
    {
        var detectedPublishers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(installationPath) || !Directory.Exists(installationPath))
        {
            return Task.FromResult(detectedPublishers);
        }

        var executableFiles = Directory.GetFiles(installationPath, "*.exe", SearchOption.TopDirectoryOnly);

        foreach (var executablePath in executableFiles)
        {
            foreach (var identifier in gameClientIdentifiers)
            {
                if (identifier.CanIdentify(executablePath))
                {
                    detectedPublishers.Add(identifier.PublisherId);
                    logger.LogDebug(
                        "Detected {PublisherId} executable at {Path}",
                        identifier.PublisherId,
                        executablePath);
                    break; // Each executable matches at most one identifier
                }
            }
        }

        return Task.FromResult(detectedPublishers);
    }

    /// <summary>
    /// Checks manifest pool for existing downloaded GameClient content from a specific publisher.
    /// Filters out local detection manifests (version "Auto-Updated" or 0) - only returns
    /// manifests that were downloaded from the publisher's CDN with real version numbers.
    /// </summary>
    /// <param name="publisherId">The publisher ID to check for.</param>
    /// <param name="gameType">The game type to filter by (optional - pass Unknown to get all).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of downloaded GameClient manifests from the publisher.</returns>
    private async Task<List<ContentManifest>> GetExistingPublisherManifestsAsync(
        string publisherId,
        GameType gameType,
        CancellationToken cancellationToken)
    {
        var manifests = new List<ContentManifest>();

        try
        {
            var allManifestsResult = await contentManifestPool.GetAllManifestsAsync(cancellationToken);
            if (!allManifestsResult.Success || allManifestsResult.Data == null)
            {
                return manifests;
            }

            manifests =
            [
                .. allManifestsResult.Data
                    .Where(m =>
                        m.ContentType == ContentType.GameClient &&
                        string.Equals(m.Publisher?.PublisherType, publisherId, StringComparison.OrdinalIgnoreCase) &&

                        // and their ID doesn't start with "1.0." (version 0)
                        GenHub.Core.Helpers.ManifestHelper.IsDownloadedManifest(m)),
            ];

            logger.LogDebug(
                "Found {Count} downloaded {Publisher} GameClient manifests in pool for {GameType}",
                manifests.Count,
                publisherId,
                gameType);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking manifest pool for {Publisher} content", publisherId);
        }

        return manifests;
    }

    /// <summary>
    /// Creates GameClient objects from existing manifests in the pool.
    /// </summary>
    /// <param name="manifests">The manifests to create GameClients from.</param>
    /// <param name="installation">The game installation context.</param>
    /// <param name="installationPath">The installation path for working directory.</param>
    /// <returns>List of GameClient objects created from the manifests.</returns>
    private List<GameClient> CreateGameClientsFromManifests(
        List<ContentManifest> manifests,
        GameInstallation installation,
        string installationPath)
    {
        var gameClients = new List<GameClient>();

        foreach (var manifest in manifests)
        {
            // Find the executable file in the manifest
            var executableFile = manifest.Files?.FirstOrDefault(f =>
                f.IsExecutable ||
                (f.RelativePath?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ?? false));

            if (executableFile == null)
            {
                logger.LogWarning(
                    "Manifest {ManifestId} has no executable file, skipping GameClient creation",
                    manifest.Id);
                continue;
            }

            var executablePath = Path.Combine(installationPath, executableFile.RelativePath ?? string.Empty);

            var gameClient = new GameClient
            {
                Id = manifest.Id.Value,
                Name = manifest.Name,
                Version = manifest.Version ?? GameClientConstants.AutoDetectedVersion,
                ExecutablePath = executablePath,
                GameType = manifest.TargetGame,
                InstallationId = installation.Id,
                WorkingDirectory = installationPath,
                SourceType = ContentType.GameClient,
            };

            gameClients.Add(gameClient);
            logger.LogDebug(
                "Created GameClient from manifest: {ManifestId} -> {GameClientName}",
                manifest.Id,
                gameClient.Name);
        }

        return gameClients;
    }

    /// <summary>
    /// Recursively finds game executables in a directory, skipping excluded folders.
    /// </summary>
    /// <param name="rootPath">The root directory to search.</param>
    /// <returns>List of paths to game executables found.</returns>
    private List<string> FindGameExecutablesRecursively(string rootPath)
    {
        var results = new List<string>();
        var directoriesToProcess = new Queue<string>();
        directoriesToProcess.Enqueue(rootPath);

        while (directoriesToProcess.Count > 0)
        {
            var currentDir = directoriesToProcess.Dequeue();

            try
            {
                // Process files in current directory
                foreach (var file in Directory.EnumerateFiles(currentDir))
                {
                    if (hashRegistry.PossibleExecutableNames.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
                    {
                        results.Add(file);
                    }
                }

                // Enqueue subdirectories if not excluded
                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    var dirName = Path.GetFileName(subDir);
                    if (!_excludedDirectories.Contains(dirName))
                    {
                        directoriesToProcess.Enqueue(subDir);
                    }
                    else
                    {
                        logger.LogDebug("Skipping excluded directory during game client scan: {Directory}", subDir);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to scan directory {Directory} for game clients", currentDir);
            }
        }

        return results;
    }
}
