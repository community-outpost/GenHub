using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
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
    ILogger<GameClientDetector> logger) : IGameClientDetector
{
    private readonly IManifestGenerationService _manifestGenerationService = manifestGenerationService;
    private readonly IContentManifestPool _contentManifestPool = contentManifestPool;
    private readonly IFileHashProvider _hashProvider = hashProvider;
    private readonly IGameClientHashRegistry _hashRegistry = hashRegistry;
    private readonly ILogger<GameClientDetector> _logger = logger;

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
                        Name = $"{inst.InstallationType} Generals {version}",
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
                    _logger.LogWarning("Skipping Generals game client for {InstallationId}: no valid executable found at {ExePath}", inst.Id, actualExePath);
                }

                // Then check for GeneralsOnline clients in the installation directory
                var generalsOnlineClients = await DetectGeneralsOnlineClientsAsync(inst, GameType.Generals, cancellationToken);
                gameClients.AddRange(generalsOnlineClients);
            }

            if (inst.HasZeroHour && !string.IsNullOrEmpty(inst.ZeroHourPath) && Directory.Exists(inst.ZeroHourPath))
            {
                // First, detect the standard installation client (priority over GeneralsOnline for auto-selection)
                var (version, actualExePath) = await DetectVersionFromInstallationAsync(inst.ZeroHourPath, GameType.ZeroHour, cancellationToken);
                if (File.Exists(actualExePath))
                {
                    var zeroHourVersion = new GameClient
                    {
                        Name = $"{inst.InstallationType} Zero Hour {version}",
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
                    _logger.LogWarning("Skipping Zero Hour game client for {InstallationId}: no valid executable found at {ExePath}", inst.Id, actualExePath);
                }

                // Then check for GeneralsOnline clients in the installation directory
                var generalsOnlineClients = await DetectGeneralsOnlineClientsAsync(inst, GameType.ZeroHour, cancellationToken);
                gameClients.AddRange(generalsOnlineClients);
            }
        }

        stopwatch.Stop();
        _logger.LogInformation("Detected {Count} game clients from {InstallCount} installations in {ElapsedMs}ms", gameClients.Count, installations.Count(), stopwatch.ElapsedMilliseconds);

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

        // Search for all possible executable names
        var allFiles = await Task.Run(() =>
            Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Where(f => _hashRegistry.PossibleExecutableNames
                    .Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                .ToList());

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
        _logger.LogInformation("Scanned directory {Path} and found {Count} game clients in {ElapsedMs}ms", path, gameClients.Count, stopwatch.ElapsedMilliseconds);
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

            var hash = await _hashProvider.ComputeFileHashAsync(executablePath, cancellationToken);

            // Try to detect version for both game types
            var generalsVersion = _hashRegistry.GetVersionFromHash(hash, GameType.Generals);
            var zeroHourVersion = _hashRegistry.GetVersionFromHash(hash, GameType.ZeroHour);
            GameType detectedGameType;
            string detectedVersion;
            if (!string.Equals(generalsVersion, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                detectedGameType = GameType.Generals;
                detectedVersion = generalsVersion;
            }
            else if (!string.Equals(zeroHourVersion, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                detectedGameType = GameType.ZeroHour;
                detectedVersion = zeroHourVersion;
            }
            else
            {
                detectedGameType = GameType.Unknown;
                detectedVersion = "Unknown";
            }

            if (detectedGameType != GameType.Unknown && !string.Equals(detectedVersion, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                var gameTypeName = detectedGameType == GameType.Generals ? "Generals" : "Zero Hour";
                _logger.LogDebug("Detected {GameType} {Version} from {ExecutablePath} with hash {Hash}", gameTypeName, detectedVersion, executablePath, hash);
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
            _logger.LogDebug("Unknown game executable found at {ExecutablePath} with hash {Hash}", executablePath, hash);
            return new GameClient
            {
                Name = $"Unknown Game ({Path.GetFileName(workingDirectory)})",
                Id = string.Empty, // Will be set by manifest generation
                Version = "Unknown",
                ExecutablePath = executablePath,
                GameType = GameType.Generals, // Default assumption
                WorkingDirectory = workingDirectory,
                InstallationId = string.Empty,
                SourceType = ContentType.GameClient,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to analyze executable {ExecutablePath}", executablePath);
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
                _logger.LogError("GameClient {ClientName} has no executable path - cannot generate manifest", gameClient.Name);
                gameClient.Id = Guid.NewGuid().ToString(); // Fallback
                return;
            }

            if (!File.Exists(gameClient.ExecutablePath))
            {
                _logger.LogError("GameClient executable not found at {ExecutablePath} - cannot generate manifest", gameClient.ExecutablePath);
                gameClient.Id = Guid.NewGuid().ToString(); // Fallback
                return;
            }

            // Generate GameClient manifest with executable included
            var builder = await _manifestGenerationService.CreateGameClientManifestAsync(
                clientPath, gameType, gameClient.Name, gameClient.Version, gameClient.ExecutablePath);

            var manifest = builder.Build();
            manifest.ContentType = ContentType.GameClient;

            var manifestVersion = gameType == GameType.ZeroHour
                ? ManifestConstants.ZeroHourManifestVersion
                : ManifestConstants.GeneralsManifestVersion;

            // Use ManifestIdGenerator for deterministic client ID generation
            if (installation != null)
            {
                var clientIdResult = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, manifestVersion, ContentType.GameClient);
                manifest.Id = ManifestId.Create(clientIdResult);
            }
            else
            {
                // For scanned/standalone clients, generate ID based on path and hash
                var fallbackId = $"scanned-{gameType.ToString().ToLowerInvariant()}-{Path.GetFileName(clientPath)}-{gameClient.Version}";
                manifest.Id = ManifestId.Create(fallbackId);
            }

            // Add to pool
            var addResult = await _contentManifestPool.AddManifestAsync(manifest, clientPath);
            if (addResult.Success)
            {
                gameClient.Id = manifest.Id.ToString();
                _logger.LogDebug("Generated GameClient manifest ID {Id} for {VersionName}", gameClient.Id, gameClient.Name);
            }
            else
            {
                _logger.LogWarning("Failed to pool GameClient manifest for {VersionName}: {Errors}", gameClient.Name, string.Join(", ", addResult.Errors));
                gameClient.Id = Guid.NewGuid().ToString(); // Fallback
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate manifest for {VersionName}", gameClient.Name);
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
    /// <returns>A tuple containing the detected version string and the actual executable path found, or ("Unknown", original path) if not recognized.</returns>
    private async Task<(string version, string executablePath)> DetectVersionFromInstallationAsync(string installationPath, GameType gameType, CancellationToken cancellationToken)
    {
        // Use the possible executable names from the registry
        foreach (var executableName in _hashRegistry.PossibleExecutableNames)
        {
            var executablePath = Path.Combine(installationPath, executableName);
            if (!File.Exists(executablePath))
                continue;

            try
            {
                // Get the actual filename with correct casing from the filesystem
                var actualFileName = Path.GetFileName(new FileInfo(executablePath).FullName);
                var actualExecutablePath = Path.Combine(installationPath, actualFileName);

                var hash = await _hashProvider.ComputeFileHashAsync(actualExecutablePath, cancellationToken);
                if (string.IsNullOrEmpty(hash))
                {
                    _logger.LogWarning("Failed to compute hash for {ExecutablePath}", actualExecutablePath);
                    continue;
                }

                var version = _hashRegistry.GetVersionFromHash(hash, gameType);

                if (!string.Equals(version, "Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "Detected {GameType} version {Version} from {ExecutableName} with hash {Hash}",
                        gameType,
                        version,
                        actualFileName,
                        hash);
                    return (version, actualExecutablePath);
                }
                else
                {
                    _logger.LogDebug(
                        "Unknown hash for {GameType} in {ExecutableName}: {Hash}",
                        gameType,
                        actualFileName,
                        hash);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to hash file {ExecutablePath}", executablePath);
            }
        }

        // If no recognized executable found, fall back to standard executable name for the game type
        var defaultExecutableName = gameType == GameType.Generals ? GameClientConstants.GeneralsExecutable : GameClientConstants.ZeroHourExecutable;
        var defaultPath = Path.Combine(installationPath, defaultExecutableName);
        _logger.LogInformation(
            "No recognized executable found for {GameType} in {InstallationPath}, using default {ExecutableName}",
            gameType,
            installationPath,
            defaultExecutableName);
        return ("Unknown", defaultPath);
    }

    /// <summary>
    /// Detects GeneralsOnline game clients by name in the game installation directory.
    /// This helper enables detection of GeneralsOnline executables that users already have
    /// from the existing GeneralsOnline launcher.
    /// </summary>
    /// <param name="installation">The game installation to scan.</param>
    /// <param name="gameType">The type of game (Generals or ZeroHour).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of detected GeneralsOnline game clients.</returns>
    /// <remarks>
    /// GeneralsOnline executables are auto-updated by the GeneralsOnline launcher,
    /// which can invalidate hash verification. For now, we detect by filename only
    /// and skip hash validation until a dedicated publisher system is implemented.
    /// </remarks>
    private async Task<List<GameClient>> DetectGeneralsOnlineClientsAsync(
        GameInstallation installation,
        GameType gameType,
        CancellationToken cancellationToken)
    {
        var detectedClients = new List<GameClient>();
        var installationPath = gameType == GameType.Generals ? installation.GeneralsPath : installation.ZeroHourPath;

        if (string.IsNullOrEmpty(installationPath) || !Directory.Exists(installationPath))
        {
            return detectedClients;
        }

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
                var variantName = executableName.ToLowerInvariant() switch
                {
                    GameClientConstants.GeneralsOnline30HzExecutable => GameClientConstants.GeneralsOnline30HzDisplayName,
                    GameClientConstants.GeneralsOnline60HzExecutable => GameClientConstants.GeneralsOnline60HzDisplayName,
                    _ => GameClientConstants.GeneralsOnlineDefaultDisplayName,
                };

                _logger.LogInformation(
                    "Detected GeneralsOnline client: {VariantName} at {ExecutablePath}",
                    variantName,
                    executablePath);

                // Format display name: "GeneralsOnline 60Hz"
                var displayName = $"{variantName}";

                var gameClient = new GameClient
                {
                    Name = displayName,
                    Id = string.Empty, // Will be set by manifest generation
                    Version = "Automatically added",
                    ExecutablePath = executablePath,
                    GameType = gameType,
                    InstallationId = installation.Id,
                    WorkingDirectory = installationPath,
                    SourceType = ContentType.GameClient,
                };

                // Generate manifest for this GeneralsOnline client
                await GenerateGeneralsOnlineClientManifestAsync(gameClient, installationPath, installation, gameType);
                detectedClients.Add(gameClient);

                _logger.LogDebug(
                    "Added GeneralsOnline game client {VariantName} with ID {ClientId}",
                    variantName,
                    gameClient.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to detect GeneralsOnline client at {ExecutablePath}",
                    executablePath);
            }
        }

        if (detectedClients.Count > 0)
        {
            _logger.LogInformation(
                "Detected {Count} GeneralsOnline clients in {InstallationPath}",
                detectedClients.Count,
                installationPath);
        }

        return detectedClients;
    }

    /// <summary>
    /// Generates a manifest for a GeneralsOnline game client with special handling.
    /// </summary>
    /// <param name="gameClient">The GeneralsOnline game client.</param>
    /// <param name="clientPath">The client installation path.</param>
    /// <param name="installation">The parent game installation.</param>
    /// <param name="gameType">The game type.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task GenerateGeneralsOnlineClientManifestAsync(
        GameClient gameClient,
        string clientPath,
        GameInstallation installation,
        GameType gameType)
    {
        try
        {
            // Validate that the GameClient has a valid executable path
            if (string.IsNullOrWhiteSpace(gameClient.ExecutablePath))
            {
                _logger.LogError("GeneralsOnline client {ClientName} has no executable path - cannot generate manifest", gameClient.Name);
                gameClient.Id = Guid.NewGuid().ToString(); // Fallback
                return;
            }

            if (!File.Exists(gameClient.ExecutablePath))
            {
                _logger.LogError("GeneralsOnline executable not found at {ExecutablePath} - cannot generate manifest", gameClient.ExecutablePath);
                gameClient.Id = Guid.NewGuid().ToString(); // Fallback
                return;
            }

            // Generate GeneralsOnline-specific manifest with executable included
            var builder = await _manifestGenerationService.CreateGeneralsOnlineClientManifestAsync(
                clientPath,
                gameType,
                gameClient.Name,
                gameClient.Version,
                gameClient.ExecutablePath);

            var manifest = builder.Build();
            manifest.ContentType = ContentType.GameClient;

            var zhDependency = new ContentDependency
            {
                Id = ManifestId.Create(ManifestConstants.DefaultContentDependencyId),
                Name = GameClientConstants.ZeroHourInstallationDependencyName,
                DependencyType = ContentType.GameInstallation,
                InstallBehavior = DependencyInstallBehavior.RequireExisting,
                CompatibleGameTypes = [GameType.ZeroHour],
            };
            manifest.Dependencies.Add(zhDependency);

            var manifestVersion = gameType == GameType.ZeroHour
                ? ManifestConstants.ZeroHourManifestVersion
                : ManifestConstants.GeneralsManifestVersion;

            // Generate deterministic ID for GeneralsOnline client
            // Use publisher-based content ID format: version.publisher.contentType.contentName
            // This allows multiple GeneralsOnline variants (30Hz, 60Hz)
            var executableName = Path.GetFileNameWithoutExtension(gameClient.ExecutablePath).ToLowerInvariant();

            // Replace underscores with dashes for manifest ID format compliance
            var safeExecutableName = executableName.Replace("_", "-");
            var clientIdResult = ManifestIdGenerator.GeneratePublisherContentId(
                PublisherTypeConstants.GeneralsOnline,
                ContentType.GameClient,
                $"{gameType.ToString().ToLowerInvariant()}-{safeExecutableName}");
            manifest.Id = ManifestId.Create(clientIdResult);

            // Add to pool
            var addResult = await _contentManifestPool.AddManifestAsync(manifest, clientPath);
            if (addResult.Success)
            {
                gameClient.Id = manifest.Id.ToString();
                _logger.LogDebug("Generated GeneralsOnline manifest ID {Id} for {ClientName}", gameClient.Id, gameClient.Name);
            }
            else
            {
                _logger.LogWarning("Failed to pool GeneralsOnline manifest for {ClientName}: {Errors}", gameClient.Name, string.Join(", ", addResult.Errors));
                gameClient.Id = Guid.NewGuid().ToString(); // Fallback
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate manifest for GeneralsOnline client {ClientName}", gameClient.Name);
            gameClient.Id = Guid.NewGuid().ToString(); // Fallback
        }
    }
}