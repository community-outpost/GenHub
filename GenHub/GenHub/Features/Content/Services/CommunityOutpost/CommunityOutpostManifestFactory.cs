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
    IControlBarPackageProcessor controlBarProcessor) : IPublisherManifestFactory
{
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
            var dependencyBigFiles = CollectDependencyBigFiles(contentMetadata);

            var alwaysIncludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (contentMetadata.Category == GenPatcherContentCategory.ControlBar)
            {
                // Small metadata BIG included alongside variant-specific files in GenPatcher builds
                alwaysIncludeFiles.Add("340_ControlBarProZH.big");
            }

            var isControlBarVariant = contentMetadata.Category == GenPatcherContentCategory.ControlBar &&
                                      contentMetadata.SupportsVariants &&
                                      variant != null;

            HashSet<string> controlBarRepackedOutputs;
            if (isControlBarVariant)
            {
                var outputs = await controlBarProcessor.ProcessAndRepackControlBarAsync(
                    extractedDirectory,
                    originalManifest,
                    variant?.Id,
                    cancellationToken);
                controlBarRepackedOutputs = new HashSet<string>(outputs, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                controlBarRepackedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (controlBarRepackedOutputs.Count > 0)
            {
                allFiles = Directory.GetFiles(extractedDirectory, "*.*", SearchOption.AllDirectories);
            }

            var hasVariantBigFiles = variant != null && HasVariantBigFiles(
                allFiles,
                variant,
                controlBarRepackedOutputs,
                alwaysIncludeFiles,
                dependencyBigFiles);

            foreach (var fullPath in allFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(extractedDirectory, fullPath);
                if (!ShouldIncludeFile(
                    relativePath,
                    variant,
                    isControlBarVariant,
                    hasVariantBigFiles,
                    dependencyBigFiles,
                    alwaysIncludeFiles,
                    controlBarRepackedOutputs))
                {
                    continue;
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
                SchemaVersion = originalManifest.SchemaVersion,
                ContentType = originalManifest.ContentType,
                TargetGame = (variant != null && variant.TargetGame.HasValue) ? variant.TargetGame.Value : originalManifest.TargetGame,
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
            if (manifest.Dependencies is { Count: > 0 })
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

    private HashSet<string> CollectDependencyBigFiles(GenPatcherContentMetadata contentMetadata)
    {
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

        return dependencyBigFiles;
    }

    private bool HasVariantBigFiles(
        string[] allFiles,
        ContentVariant variant,
        HashSet<string> controlBarRepackedOutputs,
        HashSet<string> alwaysIncludeFiles,
        HashSet<string> dependencyBigFiles)
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
                return true;
            }

            var normalized = name.ToLowerInvariant();
            if (variant.IncludePatterns?.Any(p => GetCachedRegex(p.ToLowerInvariant()).IsMatch(normalized)) == true)
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldIncludeFile(
        string relativePath,
        ContentVariant? variant,
        bool isControlBarVariant,
        bool hasVariantBigFiles,
        HashSet<string> dependencyBigFiles,
        HashSet<string> alwaysIncludeFiles,
        HashSet<string> controlBarRepackedOutputs)
    {
        var fileName = Path.GetFileName(relativePath);
        var normalizedPath = relativePath.Replace('\\', '/').ToLowerInvariant();
        var isDependencyBig = dependencyBigFiles.Contains(fileName);
        var isAlwaysInclude = alwaysIncludeFiles.Contains(fileName);
        var isRepackedOutput = controlBarRepackedOutputs.Contains(fileName);

        if (isControlBarVariant && controlBarRepackedOutputs.Count > 0 && !isRepackedOutput && !isDependencyBig && !isAlwaysInclude)
        {
            logger.LogDebug("Skipping file {File} because control bar variant is repacked into Art/Data BIG files", relativePath);
            return false;
        }

        if (isControlBarVariant && hasVariantBigFiles && !fileName.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Skipping non-BIG file {File} for control bar variant {Variant}", relativePath, variant?.Name);
            return false;
        }

        if (variant != null)
        {
            if (variant.IncludePatterns is { Count: > 0 })
            {
                bool matchesInclude = false;
                foreach (var pattern in variant.IncludePatterns)
                {
                    var regex = GetCachedRegex(pattern);
                    if (regex.IsMatch(fileName) || regex.IsMatch(normalizedPath))
                    {
                        matchesInclude = true;
                        break;
                    }
                }

                if (!matchesInclude && !isDependencyBig && !isAlwaysInclude)
                {
                    logger.LogDebug("Skipping file {File} - does not match variant {Variant} include patterns", relativePath, variant.Name);
                    return false;
                }
            }

            if (variant.ExcludePatterns is { Count: > 0 })
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
                    return false;
                }
            }
        }

        return true;
    }
}
