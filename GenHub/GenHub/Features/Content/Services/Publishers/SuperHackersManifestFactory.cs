using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Publishers;

/// <summary>
/// Manifest factory for TheSuperHackers publisher.
/// Handles multi-game releases with separate executables for Generals and Zero Hour.
/// </summary>
public class SuperHackersManifestFactory(ILogger<SuperHackersManifestFactory> logger, IFileHashProvider hashProvider) : IPublisherManifestFactory
{
    /// <summary>
    /// Extracts version number from manifest ID.
    /// </summary>
    private static int ExtractVersionFromManifestId(string manifestId)
    {
        var parts = manifestId.Split('.');
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

        // Only handle GameClient content type
        var isGameClient = manifest.ContentType == ContentType.GameClient;

        var canHandle = publisherMatches && isGameClient;

        logger.LogDebug(
            "CanHandle check for manifest {ManifestId}: Publisher={Publisher}, Type={PublisherType}, IsGameClient={IsGameClient}, Result={CanHandle}",
            manifest.Id,
            manifest.Publisher?.Name,
            manifest.Publisher?.PublisherType,
            isGameClient,
            canHandle);

        return canHandle;
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
            return new List<ContentManifest>();
        }

        logger.LogInformation("Detected {Count} game executables for SuperHackers release", detectedExecutables.Count);

        var manifests = new List<ContentManifest>();

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
            if (fileName == GameClientConstants.SuperHackersGeneralsExecutable.ToLowerInvariant())
            {
                result[GameType.Generals] = filePath;
                logger.LogInformation("Detected SuperHackers Generals executable: {Path}", filePath);
            }
            else if (fileName == GameClientConstants.SuperHackersZeroHourExecutable.ToLowerInvariant())
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
        var files = new List<ManifestFile>();
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

                // For GameClient manifests, only include the main executable and its PDB
                // Exclude all development tools and other executables
                if (originalManifest.ContentType == ContentType.GameClient)
                {
                    // Only include the main game executable and its PDB file
                    if (fileName != executableFileName && fileName != Path.ChangeExtension(executableFileName, ".pdb"))
                    {
                        logger.LogDebug("Excluding development tool {FileName} from GameClient manifest", fileName);
                        continue;
                    }
                }

                var relativePath = Path.GetRelativePath(executableDirectory, filePath);
                var fileInfo = new FileInfo(filePath);

                // Check if this is the game executable by comparing normalized full paths
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
                string fileHash = await hashProvider.ComputeFileHashAsync(filePath);

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

        var manifest = new ContentManifest
        {
            ManifestVersion = originalManifest.ManifestVersion,
            Id = ManifestId.Create(manifestId),
            Name = $"{originalManifest.Name} - {gameTypeName}",
            Version = originalManifest.Version,
            ContentType = ContentType.GameClient,
            TargetGame = gameType,
            Publisher = originalManifest.Publisher,
            Metadata = originalManifest.Metadata,
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
