using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Manifest factory for Community Outpost publisher.
/// Handles multi-content releases including game clients, addons (hotkeys, control bars), and patches.
/// </summary>
public class CommunityOutpostManifestFactory(
    ILogger<CommunityOutpostManifestFactory> logger,
    IFileHashProvider hashProvider) : IPublisherManifestFactory
{
    /// <inheritdoc />
    public string PublisherId => CommunityOutpostConstants.PublisherId;

    /// <inheritdoc />
    public bool CanHandle(ContentManifest manifest)
    {
        var publisherMatches = manifest.Publisher?.PublisherType?.Equals(
            CommunityOutpostConstants.PublisherType,
            StringComparison.OrdinalIgnoreCase) == true;

        logger.LogDebug(
            "CanHandle check for manifest {ManifestId}: Publisher={Publisher}, Type={PublisherType}, Result={Result}",
            manifest.Id,
            manifest.Publisher?.Name,
            manifest.Publisher?.PublisherType,
            publisherMatches);

        return publisherMatches;
    }

    /// <inheritdoc />
    public async Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Creating Community Outpost manifests from extracted content in: {Directory}",
            extractedDirectory);

        if (!Directory.Exists(extractedDirectory))
        {
            logger.LogError("Extracted directory does not exist: {Directory}", extractedDirectory);
            return new List<ContentManifest>();
        }

        var detectedContent = await DetectContentTypesAsync(extractedDirectory, cancellationToken);

        if (detectedContent.Count == 0)
        {
            logger.LogWarning("No Community Outpost content detected in {Directory}", extractedDirectory);
            return new List<ContentManifest>();
        }

        logger.LogInformation(
            "Detected {Count} content types for Community Outpost release",
            detectedContent.Count);

        var manifests = new List<ContentManifest>();

        foreach (var (contentType, contentFiles) in detectedContent.OrderBy(kv => kv.Key))
        {
            try
            {
                var manifest = await BuildManifestForContentTypeAsync(
                    originalManifest,
                    extractedDirectory,
                    contentType,
                    contentFiles,
                    cancellationToken);

                if (manifest != null)
                {
                    manifests.Add(manifest);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to create manifest for content type {ContentType}",
                    contentType);
            }
        }

        return manifests;
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        // For Community Outpost, content is organized by type
        return manifest.ContentType switch
        {
            ContentType.GameClient => Path.Combine(extractedDirectory, "GameClient"),
            ContentType.Addon => Path.Combine(extractedDirectory, "Addons", manifest.Name),
            ContentType.Patch => extractedDirectory,
            _ => extractedDirectory
        };
    }

    /// <summary>
    /// Extracts a numeric version from a version string like "2025-11-07" or "weekly-2025-11-21".
    /// Extracts all digits and returns them as an integer (e.g., "2025-11-07" -> 20251107).
    /// </summary>
    private static int ExtractVersionFromVersionString(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return 0;
        }

        // Extract all digits from the version string
        var digits = System.Text.RegularExpressions.Regex.Replace(version, @"\D", string.Empty);

        // Take first 8 digits (YYYYMMDD format) to avoid overflow
        if (digits.Length > 8)
        {
            digits = digits.Substring(0, 8);
        }

        return int.TryParse(digits, out var result) ? result : 0;
    }

    /// <summary>
    /// Detects different content types within the extracted directory.
    /// Looks for game executables, addon files (hotkeys, control bars), and patches.
    /// </summary>
    private async Task<Dictionary<ContentType, List<string>>> DetectContentTypesAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ContentType, List<string>>();

        try
        {
            var allFiles = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(directory, f))
                .ToList();

            // Detect game executables
            var executables = allFiles.Where(f =>
                f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                (f.Contains("generals", StringComparison.OrdinalIgnoreCase) ||
                 f.Contains("zerohour", StringComparison.OrdinalIgnoreCase) ||
                 f.Contains("game", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (executables.Any())
            {
                result[ContentType.GameClient] = executables;
                logger.LogInformation("Detected {Count} game executable(s)", executables.Count);
            }

            // Detect hotkey addons
            var hotkeyFiles = allFiles.Where(f =>
                f.Contains("hotkey", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("keyboard", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (hotkeyFiles.Any())
            {
                result[ContentType.Addon] = hotkeyFiles;
                logger.LogInformation("Detected {Count} hotkey addon file(s)", hotkeyFiles.Count);
            }

            // Detect control bar addons
            var controlBarFiles = allFiles.Where(f =>
                f.Contains("controlbar", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("commandbar", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("ui", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (controlBarFiles.Any())
            {
                if (result.ContainsKey(ContentType.Addon))
                {
                    result[ContentType.Addon].AddRange(controlBarFiles);
                }
                else
                {
                    result[ContentType.Addon] = controlBarFiles;
                }

                logger.LogInformation("Detected {Count} control bar addon file(s)", controlBarFiles.Count);
            }

            // Detect patches (DLL, BIG files, INI modifications)
            var patchFiles = allFiles.Where(f =>
                f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".big", StringComparison.OrdinalIgnoreCase) ||
                (f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) &&
                 !hotkeyFiles.Contains(f) &&
                 !controlBarFiles.Contains(f)))
                .ToList();

            if (patchFiles.Any())
            {
                result[ContentType.Patch] = patchFiles;
                logger.LogInformation("Detected {Count} patch file(s)", patchFiles.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error detecting content types in {Directory}", directory);
        }

        await Task.CompletedTask;
        return result;
    }

    /// <summary>
    /// Builds a manifest for a specific content type.
    /// </summary>
    private async Task<ContentManifest?> BuildManifestForContentTypeAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        ContentType contentType,
        List<string> contentFiles,
        CancellationToken cancellationToken)
    {
        if (contentFiles == null || contentFiles.Count == 0)
        {
            return null;
        }

        try
        {
            // Determine content subdirectory
            var contentDirectory = GetContentDirectory(extractedDirectory, contentType, contentFiles);

            // Extract version from original manifest Version for consistent versioning
            // Version is typically a date string like "2025-11-07" -> 20251107
            var userVersion = ExtractVersionFromVersionString(originalManifest.Version);

            // Determine content name based on content type
            var contentName = contentType switch
            {
                ContentType.GameClient => "zerohour", // Community Outpost targets Zero Hour
                ContentType.Addon => "addons",
                ContentType.Patch => "patch",
                _ => contentType.ToString().ToLowerInvariant(),
            };

            // Generate proper manifest ID using the standard generator
            var manifestId = ManifestIdGenerator.GeneratePublisherContentId(
                CommunityOutpostConstants.PublisherId,
                contentType,
                contentName,
                userVersion);

            // Create manifest name
            var manifestName = contentType switch
            {
                ContentType.GameClient => $"{CommunityOutpostConstants.ContentName} Game Client",
                ContentType.Addon => $"{CommunityOutpostConstants.ContentName} Addons",
                ContentType.Patch => CommunityOutpostConstants.ContentName,
                _ => $"{CommunityOutpostConstants.ContentName} {contentType}"
            };

            // Build file entries with hashes
            var fileEntries = new List<ManifestFile>();

            foreach (var relativePath in contentFiles)
            {
                var fullPath = Path.Combine(contentDirectory, relativePath);

                if (!File.Exists(fullPath))
                {
                    logger.LogWarning("File not found during manifest creation: {Path}", fullPath);
                    continue;
                }

                var hash = await hashProvider.ComputeFileHashAsync(fullPath, cancellationToken);
                var fileSize = new FileInfo(fullPath).Length;

                var isExecutable = relativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

                fileEntries.Add(new ManifestFile
                {
                    RelativePath = relativePath,
                    Hash = hash,
                    Size = fileSize,
                    IsExecutable = isExecutable,
                    SourceType = ContentSourceType.ExtractedPackage,
                    SourcePath = fullPath,
                });

                logger.LogDebug(
                    "Added file to manifest: {Path} (Size: {Size} bytes, Hash: {Hash})",
                    relativePath,
                    fileSize,
                    hash);
            }

            // Create the manifest
            var manifest = new ContentManifest
            {
                Id = manifestId,
                Name = manifestName,
                Version = originalManifest.Version,
                ManifestVersion = originalManifest.ManifestVersion,
                ContentType = contentType,
                TargetGame = originalManifest.TargetGame,
                Files = fileEntries,
                Dependencies = new List<ContentDependency>(),
                InstallationInstructions = originalManifest.InstallationInstructions,
                Publisher = originalManifest.Publisher,
                Metadata = new ContentMetadata
                {
                    Description = $"{manifestName} - {originalManifest.Metadata?.Description ?? string.Empty}",
                    Tags = originalManifest.Metadata?.Tags ?? new List<string>(),
                    ChangelogUrl = originalManifest.Metadata?.ChangelogUrl,
                },
            };

            logger.LogInformation(
                "Created manifest {ManifestId} for {ContentType} with {FileCount} files",
                manifestId,
                contentType,
                fileEntries.Count);

            return manifest;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to build manifest for content type {ContentType}",
                contentType);
            return null;
        }
    }

    /// <summary>
    /// Determines the content directory based on content type and files.
    /// </summary>
    private string GetContentDirectory(
        string extractedDirectory,
        ContentType contentType,
        List<string> contentFiles)
    {
        if (contentFiles.Count == 0)
        {
            return extractedDirectory;
        }

        // For game clients, find the directory containing the executable
        if (contentType == ContentType.GameClient)
        {
            var executable = contentFiles.FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (executable != null)
            {
                var executableFullPath = Path.Combine(extractedDirectory, executable);
                var directory = Path.GetDirectoryName(executableFullPath);
                return directory ?? extractedDirectory;
            }
        }

        // For addons and patches, use the common parent directory
        var firstFile = Path.Combine(extractedDirectory, contentFiles.First());
        var commonDirectory = Path.GetDirectoryName(firstFile);

        // Find common parent directory for all files
        foreach (var file in contentFiles.Skip(1))
        {
            var fullPath = Path.Combine(extractedDirectory, file);
            var fileDirectory = Path.GetDirectoryName(fullPath);

            if (fileDirectory != null && commonDirectory != null)
            {
                while (!fileDirectory.StartsWith(commonDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    commonDirectory = Path.GetDirectoryName(commonDirectory);
                    if (commonDirectory == null)
                    {
                        break;
                    }
                }
            }
        }

        return commonDirectory ?? extractedDirectory;
    }
}
