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
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
            // A combined directory flags both games at one path. Its single executable
            // set must be scanned once, as Zero Hour: running the Generals scan over the
            // same directory would wrap the very same executable in a second, duplicate
            // GameClient. Publisher detection below is unaffected — identifiers match
            // per-game executables and filter on the identified game type themselves.
            var isCombinedDirectory = inst.HasGenerals
                && inst.HasZeroHour
                && !string.IsNullOrEmpty(inst.GeneralsPath)
                && !string.IsNullOrEmpty(inst.ZeroHourPath)
                && Path.GetFullPath(inst.GeneralsPath).Equals(Path.GetFullPath(inst.ZeroHourPath), StringComparison.OrdinalIgnoreCase);

            if (inst.HasGenerals && !string.IsNullOrEmpty(inst.GeneralsPath) && Directory.Exists(inst.GeneralsPath))
            {
                if (isCombinedDirectory)
                {
                    logger.LogDebug(
                        "Skipping standard Generals client scan for {InstallationId}: {GeneralsPath} is a combined directory scanned once as Zero Hour",
                        inst.Id,
                        inst.GeneralsPath);
                }
                else
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
        var allFiles = await Task.Run(() => FindGameExecutablesRecursively(path), cancellationToken);

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
        if (string.IsNullOrEmpty(gameClient.ExecutablePath))
        {
            return Task.FromResult(false);
        }

        if (File.Exists(gameClient.ExecutablePath))
        {
            return Task.FromResult(true);
        }

        var isResolvableBundle = Directory.Exists(gameClient.ExecutablePath)
            && gameClient.ExecutablePath.EndsWith(ContentFormatConstants.MacAppBundleExtension, StringComparison.OrdinalIgnoreCase)
            && GameClientEntryDetector.ResolveBundleExecutableAbsolute(gameClient.ExecutablePath, cancellationToken) is not null;

        return Task.FromResult(isResolvableBundle);
    }

    /// <summary>
    /// Maps a client executable to a stable lowercase platform discriminator for manifest IDs.
    /// </summary>
    /// <param name="executablePath">The client executable path, if known.</param>
    /// <returns><c>windows</c>, <c>linux</c>, <c>macos</c>, or <c>unknown</c>.</returns>
    internal static string GetClientPlatformDiscriminator(string? executablePath)
    {
        var platform = ExecutableFileClassifier.DetectPlatform(executablePath ?? string.Empty);
        return platform switch
        {
            ExecutablePlatform.Windows => "windows",
            ExecutablePlatform.Linux => "linux",
            ExecutablePlatform.MacOS => "macos",
            _ => "unknown",
        };
    }

    /// <summary>
    /// Resolves the single supported Generals Online entry point among one directory's file names.
    /// The Easy Anti-Cheat bootstrapper takes precedence because it starts the binary named by
    /// <c>EasyAntiCheat/Settings.json</c>; the bare 60Hz binary is the pre-EAC fallback.
    /// </summary>
    /// <param name="fileNames">The file names present in a single directory.</param>
    /// <returns>The entry point name as it appears on disk, or <see langword="null"/> when none is present.</returns>
    private static string? ResolveGeneralsOnlineEntryPoint(IEnumerable<string> fileNames)
    {
        string? sixtyHertz = null;
        string? unixClient = null;

        foreach (var fileName in fileNames)
        {
            if (fileName.Equals(GameClientConstants.GeneralsOnlineEacLauncherExecutable, StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            if (fileName.Equals(GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase))
            {
                sixtyHertz = fileName;
            }

            if (fileName.Equals(GameClientConstants.GeneralsOnlineUnixExecutable, StringComparison.OrdinalIgnoreCase))
            {
                unixClient = fileName;
            }
        }

        return sixtyHertz ?? unixClient;
    }

    /// <summary>
    /// Resolves the single supported Generals Online entry point in a directory. Names are matched
    /// against the directory listing rather than composed from constants, so the package's own
    /// casing resolves on case-sensitive file systems.
    /// </summary>
    /// <param name="directory">The directory to inspect.</param>
    /// <returns>The entry point name, or <see langword="null"/> when none is present.</returns>
    private static string? ResolveGeneralsOnlineEntryPoint(string directory)
    {
        try
        {
            return ResolveGeneralsOnlineEntryPoint(
                Directory.EnumerateFiles(directory).Select(Path.GetFileName).OfType<string>());
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static GameClient CreateScannedClient(
        string gameTypeName,
        GameType gameType,
        string executablePath,
        string workingDirectory)
    {
        return new GameClient
        {
            Name = $"Scanned {gameTypeName} {GameClientConstants.UnknownVersion} ({Path.GetFileName(workingDirectory)})",
            Id = string.Empty, // Will be set by manifest generation
            Version = GameClientConstants.UnknownVersion,
            ExecutablePath = executablePath,
            GameType = gameType,
            WorkingDirectory = workingDirectory,
            InstallationId = string.Empty,
            SourceType = ContentType.GameClient,
        };
    }

    private static async Task<GameType> SniffSiblingEngineGameTypeAsync(string stubPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(stubPath);
        if (directory is null)
        {
            return GameType.Unknown;
        }

        var candidate = Path.Combine(directory, GameClientConstants.SteamGameDatExecutable);
        if (!candidate.TryGetFileCaseInsensitive(out var sibling) || string.IsNullOrEmpty(sibling))
        {
            return GameType.Unknown;
        }

        var inspection = await GameBinaryInspector.InspectAsync(sibling, cancellationToken);
        return inspection.Success && inspection.Data is { } verdict ? verdict.GameType : GameType.Unknown;
    }

    /// <summary>
    /// Builds publisher metadata from a game installation, if one backs the client.
    /// </summary>
    /// <param name="installation">The backing installation, if any.</param>
    /// <returns>The publisher metadata, or <see langword="null"/> without an installation.</returns>
    private static PublisherInfo? CreatePublisherInfo(GameInstallation? installation)
    {
        if (installation is null)
        {
            return null;
        }

        var (publisherName, website, supportUrl) = PublisherInfoConstants.GetPublisherInfo(installation.InstallationType);
        return new PublisherInfo
        {
            Name = publisherName,
            Website = website,
            SupportUrl = supportUrl,
            PublisherType = PublisherTypeConstants.FromInstallationType(installation.InstallationType),
        };
    }

    /// <summary>
    /// Adds the game installation dependency when an installation backs the client.
    /// </summary>
    /// <param name="manifest">The manifest to extend.</param>
    /// <param name="gameType">The type of game.</param>
    /// <param name="installation">The backing installation, if any.</param>
    private static void AddInstallationDependency(ContentManifest manifest, GameType gameType, GameInstallation? installation)
    {
        // Fix for 1.04/1.08 auto-selection
        if (installation is null)
        {
            return;
        }

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

    /// <summary>
    /// Assigns a deterministic manifest ID, scoping standalone-client IDs by platform
    /// so distinct community builds cannot alias the same identity.
    /// </summary>
    /// <param name="manifest">The manifest receiving the ID.</param>
    /// <param name="gameClient">The scanned client.</param>
    /// <param name="gameType">The type of game.</param>
    /// <param name="installation">The backing installation, if any.</param>
    private static void AssignManifestId(ContentManifest manifest, GameClient gameClient, GameType gameType, GameInstallation? installation)
    {
        // Use ManifestIdGenerator for deterministic client ID generation
        if (installation is not null)
        {
            // Use publisher-based ID generation for GameClient with correct content type
            var publisherId = installation.InstallationType.ToIdentifierString();
            var contentName = gameType == GameType.ZeroHour ? ManifestConstants.ZeroHourContentName : ManifestConstants.GeneralsContentName;

            // Convert version string to normalized integer format (e.g., "1.04" → 104, "1.08" → 108)
            int normalizedVersion = GameVersionHelper.NormalizeVersion(gameClient.Version);
            var clientIdResult = ManifestIdGenerator.GeneratePublisherContentId(publisherId, ContentType.GameClient, contentName, userVersion: normalizedVersion);
            manifest.Id = ManifestId.Create(clientIdResult);
            return;
        }

        if (!string.IsNullOrEmpty(manifest.Id.Value) && ManifestIdValidator.IsValid(manifest.Id.Value, out _))
        {
            return;
        }

        // For scanned/standalone clients without an existing valid ID, generate a valid 5-segment ID.
        // The platform discriminator keeps distinct community builds apart: without it, macOS and
        // Linux clients with the same publisher, game, and unknown version collapse to one identity
        // and alias each other (and their pool entries) through GameClient.Id.
        var fallbackPublisherId = !string.IsNullOrWhiteSpace(gameClient.PublisherType) ? gameClient.PublisherType.ToLowerInvariant() : ManifestConstants.ScannedPublisherId;
        var baseName = gameType == GameType.ZeroHour ? ManifestConstants.ZeroHourContentName : ManifestConstants.GeneralsContentName;
        var fallbackContentName = $"{baseName}-{GetClientPlatformDiscriminator(gameClient.ExecutablePath)}";
        int fallbackVersion = GameVersionHelper.NormalizeVersion(gameClient.Version);
        var fallbackIdResult = ManifestIdGenerator.GeneratePublisherContentId(fallbackPublisherId, ContentType.GameClient, fallbackContentName, userVersion: fallbackVersion);
        manifest.Id = ManifestId.Create(fallbackIdResult);
    }

    /// <summary>
    /// Enumerates the top-level files of a directory that could be launched.
    /// </summary>
    /// <param name="directoryPath">The directory to scan.</param>
    /// <returns>Full paths of the launch candidates.</returns>
    /// <remarks>
    /// Replaces the old <c>*.exe</c> glob, which hid extensionless binaries — the shape
    /// of a native Mach-O or ELF game client — from publisher detection entirely.
    /// Selection goes through <see cref="ExecutableFileClassifier.IsLegacyLaunchCandidate"/>,
    /// today a name-based rule that keeps <c>.exe</c> results identical while admitting
    /// extensionless files; a content-based (magic-byte) classification slots in at that
    /// same call without this site changing.
    /// </remarks>
    private static string[] GetLaunchCandidateFiles(string directoryPath)
    {
        return Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
            .Where(ExecutableFileClassifier.IsLegacyLaunchCandidate)
            .ToArray();
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
            var bundle = ResolveApplicationBundle(executablePath, workingDirectory, cancellationToken);
            if (bundle.Handled)
            {
                return bundle.Client;
            }

            if (!File.Exists(executablePath))
                return null;

            // Tools and installers are excluded before hashing: WorldBuilder carries full
            // engine markers and setup binaries carry branding, neither is a client.
            if (GameBinaryInspector.ClassifyFileName(Path.GetFileName(executablePath)) is not null)
            {
                logger.LogDebug("Skipping tool or installer executable {ExecutablePath}", executablePath);
                return null;
            }

            // Broadened candidates (arbitrary names outside the registry) resolve
            // publishers and reject non-clients before hashing, so a full hash pays
            // only for files that still need hash-based version identification.
            var fileName = Path.GetFileName(executablePath);
            var isRegistryName = hashRegistry.PossibleExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
            GameBinaryVerdict? preselected = null;
            if (!isRegistryName)
            {
                var early = await ResolveBroadenedEarlyAsync(executablePath, workingDirectory, cancellationToken);
                if (early.Client is not null)
                {
                    return early.Client;
                }

                if (early.Rejected)
                {
                    return null;
                }

                preselected = early.Verdict;
            }

            var hash = await hashProvider.ComputeFileHashAsync(executablePath, cancellationToken);
            var retailClient = IdentifyRetailClient(executablePath, workingDirectory, hash);
            if (retailClient is not null)
            {
                return retailClient;
            }

            // A publisher entry point is absent from the retail hash registry by definition, so an
            // unrecognized hash means "not retail" rather than "unidentifiable". Ask the publisher
            // identifiers before falling back, otherwise the GeneralsOnline anti-cheat bootstrapper
            // is reported as Unknown Game with GameType.Generals and never matches the Zero Hour
            // launch path.
            var identifiedClient = isRegistryName ? IdentifyPublisherClient(executablePath, workingDirectory) : null;
            if (identifiedClient != null)
            {
                return identifiedClient;
            }

            // Binary sniffing sits between publisher identification and the generic
            // fallback: hash-unknown engines (renamed builds, native ports) still carry
            // engine markers, while launchers and stubs resolve below instead of becoming
            // bogus Unknown Game entries.
            return await DetectGameClientFromBinaryAsync(executablePath, workingDirectory, hash, preselected, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to analyze executable {ExecutablePath}", executablePath);
            return null;
        }
    }

    /// <summary>
    /// Resolves a macOS application bundle through the publisher identifiers.
    /// Application bundles are directories without a hash, so publisher
    /// identification runs directly instead of the file cascade.
    /// </summary>
    /// <param name="executablePath">The path to the executable file.</param>
    /// <param name="workingDirectory">The working directory for the game client.</param>
    /// <param name="cancellationToken">Cancels the bundle scan.</param>
    /// <returns>Whether the path was a bundle directory, and the identified client if any.</returns>
    private (bool Handled, GameClient? Client) ResolveApplicationBundle(string executablePath, string workingDirectory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(executablePath) || File.Exists(executablePath))
        {
            return (false, null);
        }

        if (!executablePath.EndsWith(ContentFormatConstants.MacAppBundleExtension, StringComparison.OrdinalIgnoreCase))
        {
            return (true, null);
        }

        if (GameClientEntryDetector.ResolveBundleExecutableAbsolute(executablePath, cancellationToken) is null)
        {
            logger.LogWarning("Application bundle has no resolvable executable: {ExecutablePath}", executablePath);
            return (true, null);
        }

        return (true, IdentifyPublisherClient(executablePath, workingDirectory));
    }

    /// <summary>
    /// Identifies a game client by matching its file hash against the retail registry.
    /// </summary>
    /// <param name="executablePath">The path to the executable file.</param>
    /// <param name="workingDirectory">The working directory for the game client.</param>
    /// <param name="hash">The already computed file hash.</param>
    /// <returns>The retail client, or <see langword="null"/> when the hash is unrecognized.</returns>
    private GameClient? IdentifyRetailClient(string executablePath, string workingDirectory, string hash)
    {
        // Try to detect version for both game types
        var generalsVersion = hashRegistry.GetVersionFromHash(hash, GameType.Generals);
        var zeroHourVersion = hashRegistry.GetVersionFromHash(hash, GameType.ZeroHour);
        var detectedGameType = GameType.Unknown;
        var detectedVersion = GameClientConstants.UnknownVersion;
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

        if (detectedGameType == GameType.Unknown || string.Equals(detectedVersion, GameClientConstants.UnknownVersion, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

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

    /// <summary>
    /// Resolves publisher clients and rejects non-clients before hashing, returning a
    /// reusable verdict for engines that still need hash-based version identification.
    /// </summary>
    /// <param name="executablePath">The path to the executable file.</param>
    /// <param name="workingDirectory">The working directory for the game client.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The publisher client, reusable verdict, or rejection flag.</returns>
    private async Task<(GameClient? Client, GameBinaryVerdict? Verdict, bool Rejected)> ResolveBroadenedEarlyAsync(
        string executablePath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var publisherClient = IdentifyPublisherClient(executablePath, workingDirectory);
        if (publisherClient is not null)
        {
            return (publisherClient, null, false);
        }

        var preview = await GameBinaryInspector.InspectAsync(executablePath, cancellationToken);
        if (preview is not { Success: true, Data: { } verdict })
        {
            return (null, null, false);
        }

        if (verdict.Role is GameBinaryRole.Tool or GameBinaryRole.Installer or GameBinaryRole.DotNetLauncher)
        {
            logger.LogDebug("Skipping {Role} executable {ExecutablePath} before hashing: {Reason}", verdict.Role, executablePath, verdict.Reason);
            return (null, null, true);
        }

        return (null, verdict, false);
    }

    /// <summary>
    /// Classifies a hash-unknown, publisher-unknown executable by inspecting its bytes.
    /// </summary>
    /// <param name="executablePath">The path to the executable file.</param>
    /// <param name="workingDirectory">The working directory for the game client.</param>
    /// <param name="hash">The already computed file hash, for logging.</param>
    /// <param name="priorInspection">A verdict reused from the pre-hash gate, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A typed client, or null when the file is not a game client.</returns>
    private async Task<GameClient?> DetectGameClientFromBinaryAsync(
        string executablePath,
        string workingDirectory,
        string hash,
        GameBinaryVerdict? priorInspection,
        CancellationToken cancellationToken)
    {
        var inspection = priorInspection is not null
            ? OperationResult<GameBinaryVerdict>.CreateSuccess(priorInspection)
            : await GameBinaryInspector.InspectAsync(executablePath, cancellationToken);
        if (inspection.Success && inspection.Data is { } verdict)
        {
            switch (verdict.Role)
            {
                case GameBinaryRole.Tool:
                case GameBinaryRole.Installer:
                case GameBinaryRole.DotNetLauncher:
                    logger.LogDebug("Skipping {Role} executable {ExecutablePath}: {Reason}", verdict.Role, executablePath, verdict.Reason);
                    return null;
                case GameBinaryRole.PackedStub:
                    {
                        var siblingType = await SniffSiblingEngineGameTypeAsync(executablePath, cancellationToken);
                        if (siblingType is GameType.ZeroHour or GameType.Generals)
                        {
                            var siblingName = siblingType == GameType.ZeroHour ? "Zero Hour" : "Generals";
                            logger.LogDebug("Stub {ExecutablePath} resolves to a {Sibling} sibling engine", executablePath, siblingName);
                            return CreateScannedClient(siblingName, siblingType, executablePath, workingDirectory);
                        }

                        break;
                    }

                case GameBinaryRole.Engine when verdict.GameType == GameType.ZeroHour:
                    logger.LogDebug("Sniffed Zero Hour engine {ExecutablePath}: {Reason}", executablePath, verdict.Reason);
                    return CreateScannedClient("Zero Hour", GameType.ZeroHour, executablePath, workingDirectory);
                case GameBinaryRole.Engine when verdict.GameType == GameType.Generals:
                    logger.LogDebug("Sniffed Generals engine {ExecutablePath}: {Reason}", executablePath, verdict.Reason);
                    return CreateScannedClient("Generals", GameType.Generals, executablePath, workingDirectory);
                default:
                    break;
            }
        }

        // Only historically gated names get an Unknown entry: broadened scan candidates
        // without any evidence stay silent instead of flooding the scan.
        if (!IsGatedScanCandidate(executablePath))
        {
            logger.LogDebug("Skipping unevidenced executable {ExecutablePath} with hash {Hash}", executablePath, hash);
            return null;
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

    private bool IsGatedScanCandidate(string executablePath)
    {
        var fileName = Path.GetFileName(executablePath);
        return hashRegistry.PossibleExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
            || fileName.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrEmpty(Path.GetExtension(fileName)) && ExecutableFileClassifier.HasExecutableMagicBytes(executablePath));
    }

    /// <summary>
    /// Classifies an executable through the registered publisher identifiers.
    /// </summary>
    /// <param name="executablePath">The path to the executable file.</param>
    /// <param name="workingDirectory">The working directory for the game client.</param>
    /// <returns>A GameClient if a publisher recognizes the executable, otherwise null.</returns>
    private GameClient? IdentifyPublisherClient(string executablePath, string workingDirectory)
    {
        foreach (var identifier in gameClientIdentifiers)
        {
            try
            {
                // Inside the try: a throwing identifier must not stop the ones after it, and
                // the caller's handler would swallow the executable entirely.
                if (!identifier.CanIdentify(executablePath))
                {
                    continue;
                }

                var identification = identifier.Identify(executablePath);
                if (identification == null)
                {
                    continue;
                }

                logger.LogInformation(
                    "Identified {PublisherId} client {DisplayName} at {ExecutablePath}",
                    identification.PublisherId,
                    identification.DisplayName,
                    executablePath);

                return new GameClient
                {
                    Name = identification.DisplayName,
                    Id = string.Empty, // Will be set by manifest generation
                    Version = identification.LocalVersion ?? GameClientConstants.UnknownVersion,
                    ExecutablePath = executablePath,
                    GameType = identification.GameType,
                    WorkingDirectory = workingDirectory,
                    InstallationId = string.Empty,
                    SourceType = ContentType.GameClient,

                    // IsPublisherClient turns on this alone. Without it the client reads as a
                    // base retail install, so version resolution picks it as the base game and
                    // the launcher UI does not see a publisher client at all.
                    PublisherType = identification.PublisherId,
                };
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Publisher identifier {PublisherId} failed for {ExecutablePath}",
                    identifier.PublisherId,
                    executablePath);
            }
        }

        return null;
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

            var manifestExecutable = gameClient.ExecutablePath;
            if (Directory.Exists(manifestExecutable)
                && manifestExecutable.EndsWith(ContentFormatConstants.MacAppBundleExtension, StringComparison.OrdinalIgnoreCase))
            {
                manifestExecutable = GameClientEntryDetector.ResolveBundleExecutableAbsolute(manifestExecutable);
            }

            if (manifestExecutable is null || !File.Exists(manifestExecutable))
            {
                logger.LogError("GameClient executable not found at {ExecutablePath} - cannot generate manifest", gameClient.ExecutablePath);
                gameClient.Id = Guid.NewGuid().ToString(); // Fallback
                return;
            }

            // Determine publisher info from installation if available
            var publisherInfo = CreatePublisherInfo(installation);

            // Generate GameClient manifest with executable included
            var builder = await manifestGenerationService.CreateGameClientManifestAsync(
                clientPath, gameType, gameClient.Name, gameClient.Version, manifestExecutable, publisherInfo);

            var manifest = builder.Build();
            manifest.ContentType = ContentType.GameClient;

            AddInstallationDependency(manifest, gameType, installation);
            AssignManifestId(manifest, gameClient, gameType, installation);

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
        var hashResult = await DetectVersionFromHashAsync(installationPath, gameType, cancellationToken);
        if (hashResult.HasValue)
        {
            return hashResult.Value;
        }

        var defaultExecutableName = gameType == GameType.Generals
            ? GameClientConstants.GeneralsExecutable
            : GameClientConstants.ZeroHourExecutable;
        var defaultPath = Path.Combine(installationPath, defaultExecutableName);
        if (defaultPath.TryGetFileCaseInsensitive(out var resolvedDefaultPath))
        {
            defaultPath = resolvedDefaultPath;
        }

        var fallbackVersion = DetectVersionFromFileVersionInfo(defaultPath, defaultExecutableName, gameType);
        fallbackVersion = NormalizeGenericVersion(fallbackVersion, gameType);

        logger.LogInformation(
            "Using {ExecutableName} with version {Version} for {GameType}",
            defaultExecutableName,
            fallbackVersion,
            gameType);
        return (fallbackVersion, defaultPath);
    }

    private async Task<(string Version, string ExecutablePath)?> DetectVersionFromHashAsync(
        string installationPath,
        GameType gameType,
        CancellationToken cancellationToken)
    {
        foreach (var executableName in hashRegistry.PossibleExecutableNames)
        {
            var executablePath = Path.Combine(installationPath, executableName);
            if (!executablePath.TryGetFileCaseInsensitive(out var actualExecutablePath))
            {
                continue;
            }

            try
            {
                var actualFileName = Path.GetFileName(actualExecutablePath);

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

                logger.LogDebug(
                    "Unknown hash for {GameType} in {ExecutableName}: {Hash}",
                    gameType,
                    actualFileName,
                    hash);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to hash file {ExecutablePath}", executablePath);
            }
        }

        return null;
    }

    private string DetectVersionFromFileVersionInfo(string defaultPath, string defaultExecutableName, GameType gameType)
    {
        if (!File.Exists(defaultPath))
        {
            return GameClientConstants.UnknownVersion;
        }

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

                logger.LogInformation(
                    "Detected {GameType} version {Version} from FileVersionInfo for {ExecutableName}",
                    gameType,
                    cleanVersion,
                    defaultExecutableName);
                return cleanVersion;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read FileVersionInfo from {ExecutablePath}", defaultPath);
        }

        return GameClientConstants.UnknownVersion;
    }

    private string NormalizeGenericVersion(string fallbackVersion, GameType gameType)
    {
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

        return fallbackVersion;
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
        var executableFiles = GetLaunchCandidateFiles(installationPath);

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

        // Exactly one entry point per installation. Since 060526_QFE1 the Easy Anti-Cheat
        // bootstrapper wraps the 60Hz binary and both ship side by side, so detecting each
        // recognised name in turn would surface the same client twice.
        var executableName = ResolveGeneralsOnlineEntryPoint(installationPath);

        if (executableName is not null)
        {
            var executablePath = Path.Combine(installationPath, executableName);

            try
            {
                // Both supported entry points start the 60Hz client: the bootstrapper launches
                // the binary named by EasyAntiCheat/Settings.json, and pre-EAC packages run it directly.
                var variantName = GameClientConstants.GeneralsOnline60HzDisplayName;

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

        var executableFiles = GetLaunchCandidateFiles(installationPath);

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
                (f.RelativePath?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true));

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
                PublisherType = manifest.Publisher?.PublisherType ?? string.Empty,
            };

            gameClients.Add(gameClient);
            logger.LogDebug(
                "Created GameClient from manifest: {ManifestId} -> {GameClientName} (Publisher: {PublisherType})",
                manifest.Id,
                gameClient.Name,
                gameClient.PublisherType);
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
                var files = Directory.EnumerateFiles(currentDir).ToList();
                var generalsOnlineEntryPoint = ResolveGeneralsOnlineEntryPoint(
                    files.Select(Path.GetFileName).OfType<string>());

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);

                    // Arbitrarily named engines (renamed builds, recovery binaries) are
                    // candidates too, gated by MZ magic; unevidenced ones stay silent at
                    // detection time, and tools/installers are excluded by file name.
                    var isCandidate = hashRegistry.PossibleExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
                        || fileName.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase)
                        || (string.IsNullOrEmpty(Path.GetExtension(fileName)) && ExecutableFileClassifier.HasExecutableMagicBytes(file))
                        || (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && ExecutableFileClassifier.HasExecutableMagicBytes(file));

                    if (!isCandidate)
                    {
                        continue;
                    }

                    // A GeneralsOnline directory holds several supported entry points but is one
                    // client, so only the resolved entry point counts.
                    if (GameClientConstants.GeneralsOnlineExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
                        && !fileName.Equals(generalsOnlineEntryPoint, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(file);
                }

                // Enqueue subdirectories if not excluded
                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    var dirName = Path.GetFileName(subDir);
                    if (_excludedDirectories.Contains(dirName))
                    {
                        logger.LogDebug("Skipping excluded directory during game client scan: {Directory}", subDir);
                        continue;
                    }

                    // Application bundles are single clients: report the bundle itself
                    // and do not scan inside, otherwise the bundle and its inner binary
                    // would identify as two clients.
                    if (dirName.EndsWith(ContentFormatConstants.MacAppBundleExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(subDir);
                        continue;
                    }

                    directoriesToProcess.Enqueue(subDir);
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
