using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    IFileHashProvider hashProvider)
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
        // Only handle manifests with explicit thesuperhackers publisher type
        var publisherMatches = manifest.Publisher?.PublisherType?.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) == true;

        // Only handle ModdingTool content type (reclassified from GameClient)
        var isModdingTool = manifest.ContentType == ContentType.ModdingTool;

        return publisherMatches && isModdingTool;
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating SuperHackers manifests from extracted content in: {Directory}", extractedDirectory);

        var detectedExecutables = DetectGameExecutables(extractedDirectory);

        if (detectedExecutables.Count == 0)
        {
            logger.LogWarning("No SuperHackers game executables detected in {Directory}", extractedDirectory);
            return [];
        }

        logger.LogInformation("Detected {Count} game executables for SuperHackers release", detectedExecutables.Count);

        List<ContentManifest> manifests = [];

        // Sort by game type to ensure consistent ordering (Generals first, then Zero Hour)
        foreach (var (gameType, executablePath) in detectedExecutables.OrderBy(kv => kv.Key))
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

        // Create a synthetic manifest to drive the build process
        // This acts as the "original manifest" template
        var templateManifest = new ContentManifest
        {
            Id = ManifestId.Create(Guid.NewGuid().ToString()), // Temporary ID
            ManifestVersion = ManifestConstants.DefaultManifestVersion,
            Name = SuperHackersConstants.LocalInstallDisplayName,
            Version = GameClientConstants.UnknownVersion,
            ContentType = ContentType.ModdingTool,
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
    private Dictionary<GameType, string> DetectGameExecutables(string directory)
    {
        var result = new Dictionary<GameType, string>();

        if (!Directory.Exists(directory))
            return result;

        var allFiles = Directory.GetFiles(directory, "*.exe", SearchOption.AllDirectories);

        foreach (var filePath in allFiles)
        {
            var fileName = Path.GetFileName(filePath).ToLowerInvariant();

            // Check for SuperHackers executables
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

        return result;
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

                // For ModdingTool manifests, we need to include:
                // 1. The main game executable (already checked)
                // 2. Its PDB file (debug info)
                // 3. Critical DLL dependencies (dbghlp.dll, d3d8.dll, etc.)
                // 4. Game data files (.big) that might be part of the patch
                // 5. Configuration files (.ini)
                if (originalManifest.ContentType == ContentType.ModdingTool)
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
                            logger.LogDebug("Excluding irrelevant file {FileName} from ModdingTool manifest", fileName);
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
            ContentType.ModdingTool,
            gameTypeSuffix,
            userVersion: userVersion);

        logger.LogInformation(
            "Generated manifest ID for {GameType}: {ManifestId}",
            gameType,
            manifestId);

        var gameTypeName = gameType == GameType.Generals ? SuperHackersConstants.GeneralsDisplayName : SuperHackersConstants.ZeroHourDisplayName;

        // Get proper dependencies based on game type
        var dependencies = SuperHackersDependencyBuilder.GetDependenciesForGameType(gameType);

        var manifest = new ContentManifest
        {
            ManifestVersion = originalManifest.ManifestVersion,
            Id = ManifestId.Create(manifestId),
            Name = $"SuperHackers - {gameTypeName}",
            Version = originalManifest.Version,
            ContentType = ContentType.ModdingTool,
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
            },
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
