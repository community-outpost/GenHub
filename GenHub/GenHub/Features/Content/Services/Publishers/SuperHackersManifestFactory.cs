using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Manifest factory for TheSuperHackers publisher.
/// Handles multi-game releases with separate executables for Generals and Zero Hour.
/// </summary>
public class SuperHackersManifestFactory(
    ILogger<SuperHackersManifestFactory> logger,
    IFileHashProvider hashProvider,
    IArchivePayloadProcessor archivePayloadProcessor)
    : IPublisherManifestFactory
{
    /// <summary>
    /// Extracts version number from manifest ID.
    /// </summary>
    private static int ExtractVersionFromManifestId(string manifestId)
    {
        var parts = manifestId.Split(SuperHackersConstants.VersionDelimiter);
        if (parts.Length >= 2 && int.TryParse(parts[1], out int version))
        {
            return version;
        }

        return 0;
    }

    /// <inheritdoc />
    public string PublisherId => PublisherTypeConstants.TheSuperHackers;

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        // Handle manifests with explicit thesuperhackers publisher type or original provider name
        var publisherMatches = manifest.Publisher?.PublisherType?.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) == true ||
            manifest.OriginalProviderName?.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) == true;

        // Only handle GameClient content type
        var isGameClient = manifest.ContentType == ContentType.GameClient;

        return publisherMatches && isGameClient;
    }

    /// <inheritdoc />
    public Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        return CreateManifestsFromExtractedContentAsync(originalManifest, extractedDirectory, progress: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        IProgress<GenHub.Core.Models.Content.ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating SuperHackers manifests from extracted content in: {Directory}", extractedDirectory);

        if (!Directory.Exists(extractedDirectory))
        {
            logger.LogWarning("Extracted directory does not exist: {Directory}", extractedDirectory);
            return [];
        }

        // Extract archives and normalize payload directory
        await archivePayloadProcessor.ProcessPayloadAsync(
            extractedDirectory,
            originalManifest.ContentType,
            originalManifest.TargetGame,
            normalizeInactiveArchives: true,
            progress: progress,
            cancellationToken: cancellationToken);

        var detectedExecutables = DetectGameExecutables(extractedDirectory, originalManifest.TargetGame);

        if (detectedExecutables.Count == 0)
        {
            logger.LogWarning("No SuperHackers game executables detected in {Directory}", extractedDirectory);
            return [];
        }

        logger.LogInformation("Detected {Count} game executables for SuperHackers release", detectedExecutables.Count);

        // When the caller requested a specific game-client variant (the discoverer emits one card
        // per game type), build only that variant. Otherwise (local-install detection, legacy
        // single-card path) build every detected game type.
        var requestedGame = originalManifest.TargetGame;
        List<KeyValuePair<GameType, string>> buildGameTypes = (requestedGame == GameType.Generals || requestedGame == GameType.ZeroHour)
            ? [.. detectedExecutables.Where(kv => kv.Key == requestedGame).OrderBy(kv => kv.Key)]
            : [.. detectedExecutables.OrderBy(kv => kv.Key)];

        if (buildGameTypes.Count == 0)
        {
            logger.LogWarning(
                "Requested game type {GameType} not found among detected executables; falling back to all",
                requestedGame);
            buildGameTypes = [.. detectedExecutables.OrderBy(kv => kv.Key)];
        }

        List<ContentManifest> manifests = [];

        // Sort by game type to ensure consistent ordering (Generals first, then Zero Hour)
        foreach (var (gameType, executablePath) in buildGameTypes)
        {
            var manifest = await BuildManifestForGameTypeAsync(
                originalManifest,
                extractedDirectory,
                gameType,
                executablePath,
                cancellationToken);

            manifests.Add(manifest);

            logger.LogInformation(
                "Created SuperHackers {GameType} manifest {ManifestId} with {FileCount} files",
                gameType,
                manifest.Id,
                manifest.Files.Count);
        }

        return manifests;
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        // For SuperHackers, each game type has its own subdirectory based on the executable location
        var executable = manifest.Files.FirstOrDefault(f => f.IsExecutable);
        if (executable != null)
        {
            var executableFullPath = Path.Combine(extractedDirectory, executable.RelativePath);
            var directory = Path.GetDirectoryName(executableFullPath);
            return directory ?? extractedDirectory;
        }

        return extractedDirectory;
    }

    /// <summary>
    /// Creates manifests from an existing local installation without downloading.
    /// This is used when the GameClientDetector finds existing SuperHackers executables.
    /// </summary>
    /// <param name="installationPath">The path to the local installation directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of created content manifests.</returns>
    public async Task<List<ContentManifest>> CreateManifestsFromLocalInstallAsync(
        string installationPath,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating SuperHackers manifests from local install at: {Path}", installationPath);

        // Detect executables in installation path
        // For SuperHackers, the "extracted directory" logic works on installation path too
        // since the executables are direct children usually
        var detectedExecutables = DetectGameExecutables(installationPath);

        if (detectedExecutables.Count == 0)
        {
            logger.LogWarning("No SuperHackers game executables detected in {Path}", installationPath);
            return [];
        }

        logger.LogInformation("Detected {Count} game executables for SuperHackers local install", detectedExecutables.Count);

        List<ContentManifest> manifests = [];

        // Create a synthetic manifest to drive the build process.
        // This acts as the "original manifest" template; each variant will receive
        // its own final deterministic ID from BuildManifestForGameTypeAsync.
        // The template ID must be a valid 5-segment ID to pass ManifestIdValidator
        var templateManifest = new ContentManifest
        {
            Id = ManifestId.Create($"{ManifestConstants.DefaultManifestFormatVersion}.0.{PublisherTypeConstants.TheSuperHackers}.gameclient.localinstall"),
            SchemaVersion = ManifestConstants.DefaultManifestVersion,
            Name = SuperHackersConstants.LocalInstallDisplayName,
            Version = GameClientConstants.UnknownVersion,
            ContentType = ContentType.GameClient,
            Publisher = new()
            {
                Name = SuperHackersConstants.PublisherDisplayName,
                PublisherType = PublisherTypeConstants.TheSuperHackers,
            },
            Metadata = new()
            {
                Description = SuperHackersConstants.LocalInstallDescription,
                ReleaseDate = DateTime.Now,
            },
        };

        // Sort by game type to ensure consistent ordering (Generals first, then Zero Hour)
        foreach (var (gameType, executablePath) in detectedExecutables.OrderBy(kv => kv.Key))
        {
            // Build the manifest using the existing logic, but pointing to the installation path
            var manifest = await BuildManifestForGameTypeAsync(
                templateManifest,
                installationPath,
                gameType,
                executablePath,
                cancellationToken);

            manifests.Add(manifest);

            logger.LogInformation(
                "Created SuperHackers {GameType} manifest {ManifestId} from local files",
                gameType,
                manifest.Id);
        }

        return manifests;
    }

    /// <summary>
    /// Detects SuperHackers game executables in the extracted directory.
    /// </summary>
    private Dictionary<GameType, string> DetectGameExecutables(string directory, GameType targetGame = GameType.Unknown)
    {
        var result = new Dictionary<GameType, string>();

        if (!Directory.Exists(directory))
            return result;

        var allFiles = Directory.GetFiles(directory, "*.exe", SearchOption.AllDirectories);

        DetectNamedExecutables(allFiles, result);

        if (result.Count == 0)
        {
            DetectFallbackExecutables(allFiles, targetGame, result);
        }

        return result;
    }

    private void DetectNamedExecutables(IReadOnlyList<string> allFiles, Dictionary<GameType, string> result)
    {
        foreach (var filePath in allFiles)
        {
            var fileName = Path.GetFileName(filePath);

            if (string.Equals(fileName, GameClientConstants.SuperHackersGeneralsExecutable, StringComparison.OrdinalIgnoreCase))
            {
                result[GameType.Generals] = filePath;
                logger.LogInformation("Detected SuperHackers Generals executable: {Path}", filePath);
            }
            else if (string.Equals(fileName, GameClientConstants.SuperHackersZeroHourExecutable, StringComparison.OrdinalIgnoreCase))
            {
                result[GameType.ZeroHour] = filePath;
                logger.LogInformation("Detected SuperHackers Zero Hour executable: {Path}", filePath);
            }
        }
    }

    private void DetectFallbackExecutables(IReadOnlyList<string> allFiles, GameType targetGame, Dictionary<GameType, string> result)
    {
        foreach (var filePath in allFiles)
        {
            var fileName = Path.GetFileName(filePath);

            if (string.Equals(fileName, GameClientConstants.GeneralsExecutable, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, GameClientConstants.GameExecutable, StringComparison.OrdinalIgnoreCase))
            {
                var fallbackGameType = (targetGame == GameType.Generals || targetGame == GameType.ZeroHour)
                    ? targetGame
                    : GameType.ZeroHour;

                result[fallbackGameType] = filePath;
                logger.LogInformation("Detected fallback game executable '{Executable}' for SuperHackers {GameType}: {Path}", fileName, fallbackGameType, filePath);
                break;
            }
        }
    }

    /// <summary>
    /// Builds a manifest for a specific game type.
    /// </summary>
    private async Task<ContentManifest> BuildManifestForGameTypeAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        GameType gameType,
        string executablePath,
        CancellationToken cancellationToken)
    {
        List<ManifestFile> files = [];
        var executableDirectory = Path.GetDirectoryName(executablePath) ?? extractedDirectory;

        // Normalize the executable path for comparison
        var normalizedExecutablePath = Path.GetFullPath(executablePath);
        var executableFileName = Path.GetFileName(executablePath).ToLowerInvariant();

        // Get the OTHER game's executable name to exclude it (and its .pdb file)
        var otherGameExecutable = gameType == GameType.Generals
            ? GameClientConstants.SuperHackersZeroHourExecutable.ToLowerInvariant()
            : GameClientConstants.SuperHackersGeneralsExecutable.ToLowerInvariant();

        // Also exclude the .pdb file for the other game
        var otherGamePdb = Path.ChangeExtension(otherGameExecutable, ".pdb");

        logger.LogInformation(
            "Building {GameType} manifest: Main executable = {MainExe}, Excluding = {OtherExe} and {OtherPdb}",
            gameType,
            executableFileName,
            otherGameExecutable,
            otherGamePdb);

        if (Directory.Exists(executableDirectory))
        {
            var allFiles = Directory.GetFiles(executableDirectory, "*", SearchOption.AllDirectories);
            logger.LogInformation("Found {FileCount} files in {Directory} for {GameType} manifest", allFiles.Length, executableDirectory, gameType);

            foreach (var filePath in allFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var fileName = Path.GetFileName(filePath).ToLowerInvariant();

                // Skip the other game's executable and .pdb file
                if (fileName == otherGameExecutable || fileName == otherGamePdb)
                {
                    logger.LogInformation("Excluding {FileName} from {GameType} manifest", fileName, gameType);
                    continue;
                }

                // For GameClient manifests, we need to include:
                // 1. The main game executable (already checked)
                // 2. Its PDB file (debug info)
                // 3. Critical DLL dependencies (dbghlp.dll, d3d8.dll, etc.)
                // 4. Game data files (.big) that might be part of the patch
                // 5. Configuration files (.ini)
                if (originalManifest.ContentType == ContentType.GameClient)
                {
                    // Always include the main executable and its PDB
                    if (fileName == executableFileName || fileName == Path.ChangeExtension(executableFileName, ".pdb"))
                    {
                        // Keep it
                    }
                    else
                    {
                        var extension = Path.GetExtension(fileName).ToLowerInvariant();

                        // Define allowed extensions for patch dependencies
                        // .dll: required libraries (dbghlp.dll, etc.)
                        // .big: game data archives (Comanche.big, etc.)
                        // .bg:  background resources
                        // .ini: configuration
                        // .manifest: side-by-side manifests
                        var allowedExtensions = new[] { ".dll", ".big", ".bg", ".ini", ".manifest" };

                        if (!allowedExtensions.Contains(extension))
                        {
                            logger.LogDebug("Excluding irrelevant file {FileName} from GameClient manifest", fileName);
                            continue;
                        }
                    }
                }

                var relativePath = Path.GetRelativePath(executableDirectory, filePath);
                var fileInfo = new FileInfo(filePath);

                // Check if this is the game executable by comparing normalized full paths
                // For ModdingTools, we also need to ensure the executable is at the root or correctly located
                var normalizedFilePath = Path.GetFullPath(filePath);
                bool isExecutable = string.Equals(normalizedFilePath, normalizedExecutablePath, StringComparison.OrdinalIgnoreCase);

                if (isExecutable)
                {
                    logger.LogInformation("Marking {FileName} as executable for {GameType} manifest", fileName, gameType);
                    logger.LogDebug("Path comparison - File: {FilePath}, Expected: {ExpectedPath}", normalizedFilePath, normalizedExecutablePath);
                }
                else if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    // Log why other executables are NOT being marked
                    logger.LogDebug(
                        "NOT marking {FileName} as executable - Path mismatch: File={FilePath}, Expected={ExpectedPath}",
                        fileName,
                        normalizedFilePath,
                        normalizedExecutablePath);
                }

                // Compute hash for ContentAddressable storage
                string fileHash = await hashProvider.ComputeFileHashAsync(filePath, cancellationToken);

                files.Add(new ManifestFile
                {
                    RelativePath = relativePath,
                    Size = fileInfo.Length,
                    Hash = fileHash,
                    IsRequired = true,
                    IsExecutable = isExecutable,
                    SourceType = ContentSourceType.ContentAddressable,
                    SourcePath = filePath,
                });
            }
        }

        // Generate manifest ID for this game type
        var gameTypeSuffix = gameType == GameType.Generals ? SuperHackersConstants.GeneralsSuffix : SuperHackersConstants.ZeroHourSuffix;
        var userVersion = ExtractVersionFromManifestId(originalManifest.Id.Value);

        logger.LogInformation(
            "Building manifest for {GameType}: Original ID = {OriginalId}, Extracted version = {Version}, Will use suffix = {Suffix}",
            gameType,
            originalManifest.Id,
            userVersion,
            gameTypeSuffix);

        var manifestId = ManifestIdGenerator.GeneratePublisherContentId(
            PublisherTypeConstants.TheSuperHackers,
            ContentType.GameClient,
            gameTypeSuffix,
            userVersion: userVersion);

        logger.LogInformation(
            "Generated manifest ID for {GameType}: {ManifestId}",
            gameType,
            manifestId);

        var gameTypeName = gameType == GameType.Generals ? SuperHackersConstants.GeneralsDisplayName : SuperHackersConstants.ZeroHourDisplayName;

        // Get proper dependencies based on game type
        var dependencies = SuperHackersDependencyBuilder.GetDependenciesForGameType(gameType);

        var variantGroupId = $"{PublisherTypeConstants.TheSuperHackers}.{ContentType.GameClient.ToString().ToLowerInvariant()}.{userVersion}";
        var variantFamilyName = "SuperHackers";

        var variants = new List<ContentVariant>
        {
            new()
            {
                Id = SuperHackersConstants.GeneralsSuffix,
                Name = SuperHackersConstants.GeneralsDisplayName,
                VariantType = "game-type",
                Value = GameType.Generals.ToString().ToLowerInvariant(),
                IsDefault = false,
                TargetGame = GameType.Generals,
            },
            new()
            {
                Id = SuperHackersConstants.ZeroHourSuffix,
                Name = SuperHackersConstants.ZeroHourDisplayName,
                VariantType = "game-type",
                Value = GameType.ZeroHour.ToString().ToLowerInvariant(),
                IsDefault = true,
                TargetGame = GameType.ZeroHour,
            },
        };

        var variantId = gameType == GameType.Generals ? SuperHackersConstants.GeneralsSuffix : SuperHackersConstants.ZeroHourSuffix;

        var manifest = new ContentManifest
        {
            SchemaVersion = originalManifest.SchemaVersion,
            Id = ManifestId.Create(manifestId),
            Name = $"SuperHackers - {gameTypeName}",
            Version = originalManifest.Version,
            ContentType = ContentType.GameClient,
            TargetGame = gameType,
            Publisher = originalManifest.Publisher,
            Metadata = new ContentMetadata
            {
                Description = originalManifest.Metadata.Description,
                ReleaseDate = originalManifest.Metadata.ReleaseDate,
                IconUrl = SuperHackersConstants.LogoSource,
                CoverUrl = gameType == GameType.Generals ? SuperHackersConstants.GeneralsCoverSource : SuperHackersConstants.ZeroHourCoverSource,
                ThemeColor = gameType == GameType.ZeroHour ? SuperHackersConstants.ZeroHourThemeColor : SuperHackersConstants.GeneralsThemeColor,
                ScreenshotUrls = originalManifest.Metadata.ScreenshotUrls,
                Tags = originalManifest.Metadata.Tags,
                ChangelogUrl = originalManifest.Metadata.ChangelogUrl,

                // Every sibling variant carries the full variant list so the UI can render
                // the whole family from any one manifest, and declares that selection is
                // required (a user must pick a game type). SelectedVariantId marks which
                // sibling this manifest physically represents.
                Variants = variants,
                RequiresVariantSelection = true,
                SelectedVariantId = variantId,
                VariantGroupId = variantGroupId,
                VariantFamilyName = variantFamilyName,
            },
            OriginalProviderName = originalManifest.OriginalProviderName,
            OriginalContentId = originalManifest.OriginalContentId,
            SourcePath = originalManifest.SourcePath,
            Dependencies = dependencies,
            ContentReferences = originalManifest.ContentReferences,
            KnownAddons = originalManifest.KnownAddons,
            Files = files,
            RequiredDirectories = originalManifest.RequiredDirectories,
            InstallationInstructions = originalManifest.InstallationInstructions,
        };

        return await Task.FromResult(manifest);
    }
}
