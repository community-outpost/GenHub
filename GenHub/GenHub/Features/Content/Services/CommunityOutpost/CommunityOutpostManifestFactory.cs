using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Manifest factory for Community Outpost publisher.
/// Handles single-content releases (patches, addons, maps, etc.) from the GenPatcher catalog.
/// Creates manifests with proper file entries and install targets.
/// </summary>
public class CommunityOutpostManifestFactory(
    ILogger<CommunityOutpostManifestFactory> logger,
    IFileHashProvider hashProvider,
    CompressedImageToTgaConverter avifConverter) : IPublisherManifestFactory
{
    private const string ControlBarMetadataBigBase64 = "QklHRngBAAAAAAACAAAAUwAAAFMAAAEkQ29udHJvbEJhclByby50eHQAAAABdwAAAAFHZW5Ub29sXGZ1bGx2aWV3cG9ydC5kYXQAAAAAAAAAAABDb250cm9sIEJhciBQcm8gZm9yIENPTU1BTkQgQU5EIENPTlFVRVIgR0VORVJBTFM6IFpFUk8gSE9VUg0KDQpBVVRIT1I6DQpFQSBHYW1lcywgRkFTLCB4ZXpvbg0KDQpPUklHSU5BTCBET1dOTE9BRCBVUkw6DQpodHRwOi8vZ2VudG9vbC5uZXQvZG93bmxvYWQvY29udHJvbGJhcnBybw0KDQpTT1VSQ0UgQ09ERSAmIEFTU0VUUzoNCmh0dHBzOi8vZ2l0aHViLmNvbS9UaGVTdXBlckhhY2tlcnMvR2VuZXJhbHNDb250cm9sQmFyDQoNCkRPTkFUSU9OIExJTks6DQpodHRwczovL3d3dy5wYXlwYWwubWUvZ2VudG9vbA0KMQ==";
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

    private static Regex GetCachedRegex(string pattern)
    {
        var normalized = pattern.ToLowerInvariant();
        return RegexCache.GetOrAdd(normalized, p => new Regex(
            "^" + Regex.Escape(p).Replace("\\*", ".*") + "$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled));
    }

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
            "Creating Community Outpost manifest from extracted content in: {Directory}",
            extractedDirectory);

        if (!Directory.Exists(extractedDirectory))
        {
            logger.LogError("Extracted directory does not exist: {Directory}", extractedDirectory);
            return [];
        }

        // Get the content code and install target from the original manifest metadata
        var contentCode = GetContentCodeFromManifest(originalManifest);
        var contentMetadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        logger.LogInformation(
            "Processing content: {Name} ({ContentType}) with content code {Code}, InstallTarget={InstallTarget}, SupportsVariants={SupportsVariants}",
            originalManifest.Name,
            originalManifest.ContentType,
            contentCode,
            contentMetadata.InstallTarget,
            contentMetadata.SupportsVariants);

        // If content supports variants (e.g., resolution options), create separate manifests for each variant
        if (contentMetadata.SupportsVariants && contentMetadata.Variants != null && contentMetadata.Variants.Count > 0)
        {
            logger.LogInformation(
                "Creating {VariantCount} variant manifests for {Name}",
                contentMetadata.Variants.Count,
                originalManifest.Name);

            var variantManifests = new List<ContentManifest>();

            foreach (var variant in contentMetadata.Variants)
            {
                var variantManifest = await BuildManifestWithFilesAsync(
                    originalManifest,
                    extractedDirectory,
                    contentMetadata,
                    variant,
                    cancellationToken);

                if (variantManifest != null)
                {
                    variantManifests.Add(variantManifest);
                    logger.LogInformation(
                        "Created variant manifest {ManifestId} for {VariantName} with {FileCount} files",
                        variantManifest.Id,
                        variant.Name,
                        variantManifest.Files.Count);
                }
            }

            return variantManifests;
        }

        // Build the manifest with file entries (single manifest, no variants)
        var manifest = await BuildManifestWithFilesAsync(
            originalManifest,
            extractedDirectory,
            contentMetadata,
            null,
            cancellationToken);

        if (manifest == null)
        {
            logger.LogWarning("Failed to build manifest for {Name}", originalManifest.Name);
            return [];
        }

        logger.LogInformation(
            "Created manifest {ManifestId} with {FileCount} files",
            manifest.Id,
            manifest.Files.Count);

        return [manifest];
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        // Get the content code to determine the correct subdirectory
        var contentCode = GetContentCodeFromManifest(manifest);

        // Check if there's a subdirectory matching the content code
        var contentSubdir = Path.Combine(extractedDirectory, contentCode);
        if (Directory.Exists(contentSubdir))
        {
            return contentSubdir;
        }

        // Check for common subdirectory patterns (CCG for Generals, ZH for Zero Hour)
        var ccgSubdir = Path.Combine(extractedDirectory, "CCG");
        var zhSubdir = Path.Combine(extractedDirectory, "ZH");

        if (manifest.TargetGame == GameType.Generals && Directory.Exists(ccgSubdir))
        {
            return ccgSubdir;
        }

        if (manifest.TargetGame == GameType.ZeroHour && Directory.Exists(zhSubdir))
        {
            return zhSubdir;
        }

        // Default to extracted directory
        return extractedDirectory;
    }

    /// <summary>
    /// Extracts the content code from manifest metadata tags.
    /// </summary>
    private static string GetContentCodeFromManifest(ContentManifest manifest)
    {
        // Look for contentCode tag in metadata
        var contentCodeTag = manifest.Metadata?.Tags?
            .FirstOrDefault(t => t.StartsWith("contentCode:", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(contentCodeTag))
        {
            return contentCodeTag["contentCode:".Length..];
        }

        // Try to extract from manifest ID
        // Format: 1.version.communityoutpost.contentType.contentName
        var idParts = manifest.Id.Value?.Split('.') ?? [];
        if (idParts.Length >= 5)
        {
            return idParts[4]; // The content name part
        }

        return "unknown";
    }

    /// <summary>
    /// Determines the install target for a specific file based on its path and content type.
    /// </summary>
    private static ContentInstallTarget DetermineFileInstallTarget(
        string relativePath,
        ContentInstallTarget defaultTarget)
    {
        // Normalize path separators
        var normalizedPath = relativePath.Replace('\\', '/').ToLowerInvariant();

        // Map files (.map extension or in Maps folder) always go to UserMapsDirectory
        if (normalizedPath.EndsWith(".map") ||
            normalizedPath.Contains("/maps/") ||
            normalizedPath.StartsWith("maps/"))
        {
            return ContentInstallTarget.UserMapsDirectory;
        }

        // Replay files go to UserReplaysDirectory
        if (normalizedPath.EndsWith(".rep") ||
            normalizedPath.Contains("/replays/") ||
            normalizedPath.StartsWith("replays/"))
        {
            return ContentInstallTarget.UserReplaysDirectory;
        }

        // Screenshot files go to UserScreenshotsDirectory
        if ((normalizedPath.EndsWith(".bmp") || normalizedPath.EndsWith(".png") || normalizedPath.EndsWith(".jpg")) &&
            (normalizedPath.Contains("/screenshots/") || normalizedPath.StartsWith("screenshots/")))
        {
            return ContentInstallTarget.UserScreenshotsDirectory;
        }

        // Game data files (BIG, INI, etc.) go to workspace
        if (normalizedPath.EndsWith(".big") ||
            normalizedPath.EndsWith(".ini") ||
            normalizedPath.EndsWith(".exe") ||
            normalizedPath.EndsWith(".dll") ||
            normalizedPath.Contains("/data/"))
        {
            return ContentInstallTarget.Workspace;
        }

        // Use the content type's default target
        return defaultTarget;
    }

    private static string? FindControlBarVariantBigRoot(string extractedDirectory, string variantId)
    {
        var candidates = new[]
        {
            Path.Combine(extractedDirectory, "ZH", variantId, "BIG EN"),
            Path.Combine(extractedDirectory, "ZH", variantId, "BIG"),
            Path.Combine(extractedDirectory, "CCG", variantId, "BIG EN"),
            Path.Combine(extractedDirectory, "CCG", variantId, "BIG"),
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string GetControlBarVariantSuffix(string variantId)
    {
        return variantId.EndsWith("p", StringComparison.OrdinalIgnoreCase)
            ? variantId[..^1]
            : variantId;
    }

    private static bool IsAllowedControlBarBig(string fileName, string variantSuffix)
    {
        return fileName.Equals($"340_ControlBarProArt{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarProData{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarPro{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals($"340_ControlBarPro-Fix{variantSuffix}ZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("340_ControlBarProZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("400_ControlBarHDEnglishZH.big", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("400_ControlBarProCoreZH.big", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to copy a file with retry logic for transient file lock issues.
    /// </summary>
    private static async Task TryCopyFileWithRetryAsync(string source, string destination, ILogger logger, int maxRetries = 3, int delayMs = 100)
    {
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                File.Copy(source, destination, overwrite: true);
                return;
            }
            catch (IOException ex) when (attempt < maxRetries)
            {
                logger.LogWarning(
                    "File copy attempt {Attempt}/{MaxRetries} failed for {Source}: {Message}. Retrying...",
                    attempt,
                    maxRetries,
                    Path.GetFileName(source),
                    ex.Message);
                await Task.Delay(delayMs * attempt);
            }
        }

        // Final attempt without catch - let it throw if it fails
        File.Copy(source, destination, overwrite: true);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        // Recursion guard
        var sourceInfo = new DirectoryInfo(sourceDir);
        var destInfo = new DirectoryInfo(destinationDir);
        if (destInfo.FullName.StartsWith(sourceInfo.FullName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cannot copy directory into itself: Source={sourceDir}, Dest={destinationDir}");
        }

        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            try
            {
                var targetFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, targetFile, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Log and continue, or rely on caller to handle?
                // Since this is a helper, we let exceptions bubble up or just do a best-effort?
                // The comment said "doesn't handle IOException for individual files".
                // We'll throw to be safe, but at least we have the recursion guard.
                throw;
            }
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
             var targetDir = Path.Combine(destinationDir, Path.GetFileName(dir));
             CopyDirectory(dir, targetDir);
        }
    }

    /// <summary>
    /// Builds a manifest with all files from the extracted directory.
    /// If variant is provided, filters files based on variant's IncludePatterns and ExcludePatterns.
    /// </summary>
    private async Task<ContentManifest?> BuildManifestWithFilesAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        GenPatcherContentMetadata contentMetadata,
        ContentVariant? variant,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get all files from extracted directory
            var allFiles = Directory.GetFiles(extractedDirectory, "*.*", SearchOption.AllDirectories);

            if (allFiles.Length == 0)
            {
                logger.LogWarning("No files found in extracted directory: {Directory}", extractedDirectory);
                return null;
            }

            logger.LogDebug("Found {FileCount} files in extracted directory", allFiles.Length);

            var fileEntries = new List<ManifestFile>();

            var dependencyBigFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in contentMetadata.GetDependencies()
                         .Where(d => d.InstallBehavior == DependencyInstallBehavior.AutoInstall))
            {
                var depId = dependency.Id.Value;
                var lastDot = depId.LastIndexOf('.');
                if (lastDot > -1 && lastDot < depId.Length - 1)
                {
                    var depCode = depId[(lastDot + 1)..];
                    var depMetadata = GenPatcherContentRegistry.GetMetadata(depCode);
                    if (!string.IsNullOrEmpty(depMetadata.OutputFilename))
                    {
                        dependencyBigFiles.Add(depMetadata.OutputFilename);
                    }
                }
            }

            var alwaysIncludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (contentMetadata.Category == GenPatcherContentCategory.ControlBar)
            {
                // Small metadata BIG included alongside variant-specific files in GenPatcher builds
                alwaysIncludeFiles.Add("340_ControlBarProZH.big");
            }

            var controlBarRepackedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var isControlBarVariant = contentMetadata.Category == GenPatcherContentCategory.ControlBar &&
                                      contentMetadata.SupportsVariants &&
                                      variant != null;

            if (isControlBarVariant)
            {
                var variantSuffix = GetControlBarVariantSuffix(variant!.Id);
                var variantBigRoot = FindControlBarVariantBigRoot(extractedDirectory, variant.Id);

                if (!string.IsNullOrEmpty(variantBigRoot))
                {
                    // cbpr-style: Has ZH/{variant}/BIG folder structure
                    var prebuiltBigs = Directory.GetFiles(variantBigRoot, "*.big", SearchOption.TopDirectoryOnly)
                        .Where(path => IsAllowedControlBarBig(Path.GetFileName(path), variantSuffix))
                        .ToArray();

                    if (prebuiltBigs.Length > 0)
                    {
                        logger.LogInformation(
                            "Using prebuilt control bar BIG files from {VariantRoot}",
                            variantBigRoot);

                        foreach (var prebuiltBig in prebuiltBigs)
                        {
                            var bigName = Path.GetFileName(prebuiltBig);
                            var targetPath = Path.Combine(extractedDirectory, bigName);

                            // Skip copy if source and target are the same file (already in root)
                            if (!string.Equals(Path.GetFullPath(prebuiltBig), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                            {
                                await TryCopyFileWithRetryAsync(prebuiltBig, targetPath, logger);
                            }

                            controlBarRepackedOutputs.Add(bigName);
                        }
                    }
                    else
                    {
                        // No prebuilt BIGs in variant root, need to repack from source
                        var artBigName = $"340_ControlBarProArt{variantSuffix}ZH.big";
                        var dataBigName = $"340_ControlBarProData{variantSuffix}ZH.big";

                        var artBigPath = Path.Combine(extractedDirectory, artBigName);
                        var dataBigPath = Path.Combine(extractedDirectory, dataBigName);

                        if (!File.Exists(artBigPath) || !File.Exists(dataBigPath))
                        {
                            logger.LogInformation(
                                "Repacking control bar variant {Variant} into Art/Data BIG files",
                                variant.Name);

                            var artSource = Path.Combine(variantBigRoot, "Art");
                            var dataSource = Path.Combine(variantBigRoot, "Data");
                            var windowSource = Path.Combine(variantBigRoot, "Window");
                            var genToolSource = Path.Combine(variantBigRoot, "GenTool");

                            var tempRoot = Path.Combine(extractedDirectory, $"cbpro-pack-{variant.Id}");
                            var artPackRoot = Path.Combine(tempRoot, "ArtPack");
                            var dataPackRoot = Path.Combine(tempRoot, "DataPack");

                            if (Directory.Exists(tempRoot))
                            {
                                Directory.Delete(tempRoot, recursive: true);
                            }

                            Directory.CreateDirectory(artPackRoot);
                            Directory.CreateDirectory(dataPackRoot);

                            if (Directory.Exists(artSource))
                            {
                                CopyDirectory(artSource, Path.Combine(artPackRoot, "Art"));
                            }

                            if (Directory.Exists(dataSource))
                            {
                                CopyDirectory(dataSource, Path.Combine(dataPackRoot, "Data"));
                            }

                            if (Directory.Exists(windowSource))
                            {
                                CopyDirectory(windowSource, Path.Combine(dataPackRoot, "Window"));
                            }

                            if (Directory.Exists(genToolSource))
                            {
                                CopyDirectory(genToolSource, Path.Combine(dataPackRoot, "GenTool"));
                            }

                            try
                            {
                                // Convert AVIF to TGA prior to packing to ensure game compatibility
                                await avifConverter.ConvertDirectoryAsync(artPackRoot, cancellationToken);
                                await avifConverter.ConvertDirectoryAsync(dataPackRoot, cancellationToken);

                                await BigFilePacker.PackAsync(artPackRoot, artBigPath);
                                await BigFilePacker.PackAsync(dataPackRoot, dataBigPath);
                            }
                            finally
                            {
                                try
                                {
                                    if (Directory.Exists(tempRoot))
                                    {
                                        Directory.Delete(tempRoot, recursive: true);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "Failed to cleanup temp root {TempRoot}", tempRoot);
                                }
                            }
                        }

                        if (File.Exists(artBigPath))
                        {
                            controlBarRepackedOutputs.Add(artBigName);
                        }

                        if (File.Exists(dataBigPath))
                        {
                            controlBarRepackedOutputs.Add(dataBigName);
                        }
                    }
                }
                else
                {
                    // cbpx-style: Flat structure with BIG files in root (no ZH/{variant}/BIG folders)
                    logger.LogInformation(
                        "Control bar has flat structure (cbpx-style), searching for prebuilt BIG files in root");

                    var prebuiltCandidates = Directory.GetFiles(extractedDirectory, "*ControlBarPro*ZH.big", SearchOption.TopDirectoryOnly)
                        .Where(path => IsAllowedControlBarBig(Path.GetFileName(path), variantSuffix))
                        .ToArray();

                    // Check if Art/Data split files exist - prefer them over monolithic
                    var hasArtDataSplit = prebuiltCandidates.Any(p =>
                        Path.GetFileName(p).StartsWith("340_ControlBarProArt", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileName(p).StartsWith("340_ControlBarProData", StringComparison.OrdinalIgnoreCase));

                    if (hasArtDataSplit)
                    {
                        // Filter OUT monolithic files when Art/Data split exists
                        // Monolithic pattern: 340_ControlBarPro{variant}ZH.big (no Art/Data/Fix suffix)
                        prebuiltCandidates = [.. prebuiltCandidates
                            .Where(p =>
                            {
                                var name = Path.GetFileName(p);

                                // Keep Art/Data split files
                                if (name.StartsWith("340_ControlBarProArt", StringComparison.OrdinalIgnoreCase) ||
                                    name.StartsWith("340_ControlBarProData", StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }

                                // Keep fix files (cbpr-style, but may exist)
                                if (name.Contains("-Fix", StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }

                                // Keep metadata BIG (tiny file, no variant suffix)
                                if (name.Equals("340_ControlBarProZH.big", StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }

                                // Exclude monolithic files (340_ControlBarPro{variant}ZH.big without Art/Data)
                                logger.LogDebug(
                                    "Excluding monolithic BIG {Name} in favor of Art/Data split files",
                                    name);
                                return false;
                            })];
                    }

                    if (prebuiltCandidates.Length > 0)
                    {
                        logger.LogInformation(
                            "Using {Count} prebuilt control bar BIG files from flat structure: {Files}",
                            prebuiltCandidates.Length,
                            string.Join(", ", prebuiltCandidates.Select(Path.GetFileName)));

                        foreach (var candidate in prebuiltCandidates)
                        {
                            controlBarRepackedOutputs.Add(Path.GetFileName(candidate));
                        }
                    }
                    else
                    {
                        logger.LogWarning(
                            "No prebuilt control bar BIG files found for variant {Variant} in flat structure",
                            variant.Name);
                    }
                }

                // Explicitly ensure the metadata BIG file (340_ControlBarProZH.big) is included
                // This file may be in the root, ZH folder, or variant subfolder
                var metadataFileName = "340_ControlBarProZH.big";
                var metadataTargetPath = Path.Combine(extractedDirectory, metadataFileName);

                if (!File.Exists(metadataTargetPath))
                {
                    // Search for metadata file in common locations
                    var metadataSearchPaths = new[]
                    {
                        Path.Combine(extractedDirectory, "ZH", metadataFileName),
                        Path.Combine(extractedDirectory, "CCG", metadataFileName),
                        Path.Combine(extractedDirectory, "ZH", variant!.Id, metadataFileName),
                        Path.Combine(extractedDirectory, "CCG", variant!.Id, metadataFileName),
                        Path.Combine(extractedDirectory, "ZH", variant!.Id, "BIG EN", metadataFileName),
                        Path.Combine(extractedDirectory, "ZH", variant!.Id, "BIG", metadataFileName),
                        Path.Combine(extractedDirectory, "CCG", variant!.Id, "BIG EN", metadataFileName),
                        Path.Combine(extractedDirectory, "CCG", variant!.Id, "BIG", metadataFileName),
                    };

                    foreach (var searchPath in metadataSearchPaths)
                    {
                        if (File.Exists(searchPath))
                        {
                            logger.LogInformation(
                                "Found Control Bar metadata file at {SourcePath}, copying to root",
                                searchPath);

                            await TryCopyFileWithRetryAsync(searchPath, metadataTargetPath, logger);

                            break;
                        }
                    }
                }

                // Ensure metadata file is tracked in outputs if it exists
                if (File.Exists(metadataTargetPath))
                {
                    controlBarRepackedOutputs.Add(metadataFileName);
                    logger.LogInformation(
                        "Including Control Bar metadata file {FileName} in manifest",
                        metadataFileName);
                }
                else
                {
                    // Control Bar metadata file is missing from download - create it
                    // This is a known issue with some Community Outpost Control Bar packages
                    logger.LogWarning(
                        "Control Bar metadata file {FileName} not found in extracted content - creating fallback version",
                        metadataFileName);

                    try
                    {
                        // Base64-encoded 376-byte Control Bar metadata BIG file (340_ControlBarProZH.big)
                        // This identifies the mod to GenTool and prevents the "Control Bar Pro" watermark
                        var metadataBytes = Convert.FromBase64String(ControlBarMetadataBigBase64);
                        File.WriteAllBytes(metadataTargetPath, metadataBytes);

                        controlBarRepackedOutputs.Add(metadataFileName);
                        logger.LogInformation(
                            "Created Control Bar metadata file {FileName} from embedded fallback",
                            metadataFileName);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to create Control Bar metadata file - manifest will be incomplete");
                    }
                }
            }

            if (controlBarRepackedOutputs.Count > 0)
            {
                allFiles = Directory.GetFiles(extractedDirectory, "*.*", SearchOption.AllDirectories);
            }

            var hasVariantBigFiles = false;
            if (variant != null)
            {
                foreach (var path in allFiles)
                {
                    var name = Path.GetFileName(path);
                    if (!name.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (controlBarRepackedOutputs.Contains(name) ||
                        alwaysIncludeFiles.Contains(name) ||
                        dependencyBigFiles.Contains(name))
                    {
                        hasVariantBigFiles = true;
                        break;
                    }

                    var normalized = name.ToLowerInvariant();
                    if (variant.IncludePatterns != null && variant.IncludePatterns.Any(p => GetCachedRegex(p.ToLowerInvariant()).IsMatch(normalized)))
                    {
                        hasVariantBigFiles = true;
                        break;
                    }
                }
            }

            foreach (var fullPath in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(extractedDirectory, fullPath);

                var fileName = Path.GetFileName(relativePath);
                var normalizedPath = relativePath.Replace('\\', '/').ToLowerInvariant();
                var isDependencyBig = dependencyBigFiles.Contains(fileName);
                var isAlwaysInclude = alwaysIncludeFiles.Contains(fileName);
                var isControlBarVariantFile = isControlBarVariant;
                var isRepackedOutput = controlBarRepackedOutputs.Contains(fileName);

                if (isControlBarVariantFile && controlBarRepackedOutputs.Count > 0)
                {
                    if (!isRepackedOutput && !isDependencyBig && !isAlwaysInclude)
                    {
                        logger.LogDebug(
                            "Skipping file {File} because control bar variant is repacked into Art/Data BIG files",
                            relativePath);
                        continue;
                    }
                }

                if (isControlBarVariantFile && hasVariantBigFiles && !fileName.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogDebug(
                        "Skipping non-BIG file {File} for control bar variant {Variant}",
                        relativePath,
                        variant!.Name);
                    continue;
                }

                // Filter files based on variant patterns if variant is specified
                if (variant != null)
                {
                    // Check if file matches include patterns
                    bool matchesInclude = false;
                    if (variant.IncludePatterns != null && variant.IncludePatterns.Count > 0)
                    {
                        foreach (var pattern in variant.IncludePatterns)
                        {
                            var regex = GetCachedRegex(pattern);

                            if (regex.IsMatch(fileName) || regex.IsMatch(normalizedPath))
                            {
                                matchesInclude = true;
                                break;
                            }
                        }

                        // If include patterns exist but file doesn't match any, skip it
                        // UNLESS it's a dependency/base file or an always-include file
                        // File matching logic:
                        // 1. Matches inclusion pattern
                        // 2. OR: Starts with '!' (Special GenPatcher prefix for mandatory files like hotkeys)
                        // 3. AND: Is not a dependency BIG or always-include BIG (handled separately)
                        if (!matchesInclude && !fileName.StartsWith('!') && !isDependencyBig && !isAlwaysInclude)
                        {
                            logger.LogDebug("Skipping file {File} - does not match variant {Variant} include patterns", relativePath, variant.Name);
                            continue;
                        }
                    }

                    // Check if file matches exclude patterns
                    if (variant.ExcludePatterns != null && variant.ExcludePatterns.Count > 0)
                    {
                        bool matchesExclude = false;
                        foreach (var pattern in variant.ExcludePatterns)
                        {
                            var regex = GetCachedRegex(pattern);

                            if (regex.IsMatch(fileName) || regex.IsMatch(normalizedPath))
                            {
                                matchesExclude = true;
                                break;
                            }
                        }

                        if (matchesExclude && !isDependencyBig && !isAlwaysInclude)
                        {
                            logger.LogDebug("Skipping file {File} - matches variant {Variant} exclude pattern", relativePath, variant.Name);
                            continue;
                        }
                    }
                }

                var hash = await hashProvider.ComputeFileHashAsync(fullPath, cancellationToken);
                var fileSize = new FileInfo(fullPath).Length;
                var isExecutable = relativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

                // Determine install target for this file
                var fileInstallTarget = DetermineFileInstallTarget(
                    relativePath,
                    contentMetadata.InstallTarget);

                fileEntries.Add(new ManifestFile
                {
                    RelativePath = relativePath,
                    Hash = hash,
                    Size = fileSize,
                    IsExecutable = isExecutable,
                    SourceType = ContentSourceType.ExtractedPackage,
                    SourcePath = fullPath,
                    InstallTarget = fileInstallTarget,
                });

                logger.LogDebug(
                    "Added file: {Path} (Size: {Size} bytes, InstallTarget: {Target})",
                    relativePath,
                    fileSize,
                    fileInstallTarget);
            }

            // Create variant-specific manifest ID and name if variant is provided
            var manifestId = originalManifest.Id;
            var manifestName = originalManifest.Name;

            if (variant != null)
            {
                // Get the base content code from the original manifest ID
                // Format: 1.version.publisher.contentType.contentCode
                var idParts = originalManifest.Id.Value.Split('.');
                if (idParts.Length >= 5)
                {
                    var contentCode = idParts[4]; // Get the content code (e.g., "cbpx")

                    // Create new content name with variant suffix (e.g., "cbpx-1080p")
                    // This maintains the 5-segment format: schemaVersion.userVersion.publisher.contentType.contentName-variant
                    var variantContentName = $"{contentCode}-{variant.Id}";

                    // Rebuild manifest ID with variant-suffixed content name (still 5 segments)
                    manifestId = ManifestId.Create($"{idParts[0]}.{idParts[1]}.{idParts[2]}.{idParts[3]}.{variantContentName}");
                }

                // Append variant name to manifest name (e.g., "Control Bar Pro (Xezon) - 1080p")
                manifestName = $"{originalManifest.Name} - {variant.Name}";

                logger.LogInformation(
                    "Creating variant manifest: {ManifestId} ({ManifestName}) with {FileCount} files",
                    manifestId,
                    manifestName,
                    fileEntries.Count);
            }

            // Create the manifest preserving original data but with updated files
            var manifest = new ContentManifest
            {
                Id = manifestId,
                Name = manifestName,
                Version = originalManifest.Version,
                ManifestVersion = originalManifest.ManifestVersion,
                ContentType = originalManifest.ContentType,
                TargetGame = originalManifest.TargetGame,
                Files = fileEntries,

                // Remove auto-install dependencies from the list since they're bundled into the files
                Dependencies = [.. contentMetadata.GetDependencies().Where(d => d.InstallBehavior != DependencyInstallBehavior.AutoInstall)],
                InstallationInstructions = originalManifest.InstallationInstructions ?? new InstallationInstructions(),
                Publisher = originalManifest.Publisher,
                Metadata = new ContentMetadata
                {
                    Description = originalManifest.Metadata.Description,
                    ReleaseDate = originalManifest.Metadata.ReleaseDate,
                    IconUrl = CommunityOutpostConstants.LogoSource,
                    CoverUrl = CommunityOutpostConstants.CoverSource,
                    ThemeColor = CommunityOutpostConstants.ThemeColor,
                    ScreenshotUrls = originalManifest.Metadata.ScreenshotUrls,
                    Tags = originalManifest.Metadata.Tags,
                    ChangelogUrl = originalManifest.Metadata.ChangelogUrl,

                    // For variant-specific manifests, don't include the Variants list (each manifest IS a variant)
                    Variants = variant != null ? [] : (contentMetadata.Variants ?? []),
                    RequiresVariantSelection = false, // Variant already selected for this manifest
                    SelectedVariantId = variant?.Id, // Mark which variant this manifest represents
                },
            };

            logger.LogInformation(
                "Built manifest {ManifestId} for {ContentType} '{Name}' with {FileCount} files and {DependencyCount} dependencies",
                manifest.Id,
                manifest.ContentType,
                manifest.Name,
                fileEntries.Count,
                manifest.Dependencies?.Count ?? 0);

            // Log each dependency for debugging
            if (manifest.Dependencies != null && manifest.Dependencies.Count > 0)
            {
                foreach (var dep in manifest.Dependencies)
                {
                    logger.LogDebug(
                        "  Dependency: {DepName} ({DepId}) - Type: {DepType}",
                        dep.Name,
                        dep.Id,
                        dep.DependencyType);
                }
            }
            else
            {
                logger.LogWarning("Manifest {ManifestId} has NO dependencies! Category: {Category}", manifest.Id, contentMetadata.Category);
            }

            return manifest;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build manifest for {Name}", originalManifest.Name);
            return null;
        }
    }
}
