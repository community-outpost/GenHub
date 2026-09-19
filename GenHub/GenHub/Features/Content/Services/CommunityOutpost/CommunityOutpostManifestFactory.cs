using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
    public async Task<OperationResult<List<ContentManifest>>> CreateManifestsFromExtractedContentAsync(
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
            return OperationResult<List<ContentManifest>>.CreateFailure($"Extracted directory does not exist: {extractedDirectory}");
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
        if (contentMetadata.SupportsVariants && contentMetadata.Variants is { Count: > 0 })
        {
            return await CreateVariantManifestsAsync(
                originalManifest,
                extractedDirectory,
                contentMetadata,
                cancellationToken);
        }

        // Build the manifest with file entries (single manifest, no variants)
        var manifest = await BuildManifestWithFilesAsync(
            originalManifest,
            extractedDirectory,
            contentMetadata,
            null,
            null,
            cancellationToken);

        if (manifest == null)
        {
            logger.LogWarning("Failed to build manifest for {Name}", originalManifest.Name);
            return OperationResult<List<ContentManifest>>.CreateFailure($"Failed to build manifest for {originalManifest.Name}");
        }

        logger.LogInformation(
            "Created manifest {ManifestId} with {FileCount} files",
            manifest.Id,
            manifest.Files.Count);

        return OperationResult<List<ContentManifest>>.CreateSuccess([manifest]);
    }

    /// <inheritdoc />
    public string GetManifestDirectory(ContentManifest manifest, string extractedDirectory)
    {
        var contentCode = GetContentCodeFromManifest(manifest);
        var contentSubdir = Path.Combine(extractedDirectory, contentCode);
        if (Directory.Exists(contentSubdir))
        {
            return contentSubdir;
        }

        var gameSubdir = FindGameSubdirectory(extractedDirectory, manifest.TargetGame);
        if (gameSubdir != null)
        {
            return gameSubdir;
        }

        var singleSubdir = FindSingleMatchingSubdirectory(extractedDirectory, manifest.TargetGame);
        if (singleSubdir != null)
        {
            return singleSubdir;
        }

        return extractedDirectory;
    }

    private static string? FindGameSubdirectory(string extractedDirectory, GameType targetGame)
    {
        var prefixes = targetGame switch
        {
            GameType.Generals => CommunityOutpostConstants.GeneralsLanguageSubdirectories,
            GameType.ZeroHour => CommunityOutpostConstants.ZeroHourLanguageSubdirectories,
            _ => null,
        };

        if (prefixes == null)
        {
            return null;
        }

        foreach (var prefix in prefixes)
        {
            var path = Path.Combine(extractedDirectory, prefix);
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string? FindSingleMatchingSubdirectory(string extractedDirectory, GameType targetGame)
    {
        if (!Directory.Exists(extractedDirectory))
        {
            return null;
        }

        var subdirs = Directory.GetDirectories(extractedDirectory);
        if (subdirs.Length != 1)
        {
            return null;
        }

        var singleSubdir = subdirs[0];
        var dirName = Path.GetFileName(singleSubdir);
        if (targetGame == GameType.ZeroHour && dirName.EndsWith(CommunityOutpostConstants.ZeroHourDirectorySuffix, StringComparison.OrdinalIgnoreCase))
        {
            return singleSubdir;
        }

        if (targetGame == GameType.Generals && dirName.EndsWith(CommunityOutpostConstants.GeneralsDirectorySuffix, StringComparison.OrdinalIgnoreCase))
        {
            return singleSubdir;
        }

        return null;
    }

    private static Regex GetCachedRegex(string pattern)
    {
        var normalized = pattern.ToLowerInvariant();
        return RegexCache.GetOrAdd(normalized, p => new Regex(
            "^" + Regex.Escape(p).Replace("\\*", ".*") + "$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1)));
    }

    /// <summary>
    /// Extracts the content code from manifest metadata tags.
    /// </summary>
    private static string GetContentCodeFromManifest(ContentManifest manifest)
    {
        // Look for contentCode tag in metadata
        var contentCodeTag = manifest.Metadata?.Tags?
            .FirstOrDefault(t => t.StartsWith(ManifestTagConstants.ContentCodePrefix, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(contentCodeTag))
        {
            return GenPatcherContentRegistry.NormalizeContentCode(contentCodeTag[ManifestTagConstants.ContentCodePrefix.Length..]);
        }

        // Try to extract from manifest ID
        // Format: 1.version.communityoutpost.contentType.contentName
        var idParts = manifest.Id.Value?.Split('.') ?? [];
        if (idParts.Length >= 5)
        {
            return GenPatcherContentRegistry.NormalizeContentCode(idParts[4]);
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

    private static HashSet<string> CollectDependencyBigFiles(GenPatcherContentMetadata contentMetadata, GameType targetGame)
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
                if (depMetadata.TargetGame != GameType.Unknown && depMetadata.TargetGame != targetGame)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(depMetadata.OutputFilename))
                {
                    dependencyBigFiles.Add(depMetadata.OutputFilename);
                }
            }
        }

        return dependencyBigFiles;
    }

    private static bool HasVariantBigFiles(
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

    private async Task<OperationResult<List<ContentManifest>>> CreateVariantManifestsAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        GenPatcherContentMetadata contentMetadata,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Creating {VariantCount} variant manifests for {Name}",
            contentMetadata.Variants!.Count,
            originalManifest.Name);

        var variantManifests = new List<ContentManifest>();
        var isControlBarContent = contentMetadata.Category == GenPatcherContentCategory.ControlBar;
        var allControlBarOutputs = isControlBarContent ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;

        foreach (var variant in contentMetadata.Variants)
        {
            var variantManifest = await BuildManifestWithFilesAsync(
                originalManifest,
                extractedDirectory,
                contentMetadata,
                variant,
                allControlBarOutputs,
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

        if (allControlBarOutputs is { Count: > 0 })
        {
            controlBarProcessor.CleanupSourceDirectories(extractedDirectory, allControlBarOutputs);
        }

        return OperationResult<List<ContentManifest>>.CreateSuccess(variantManifests);
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
        HashSet<string>? allControlBarOutputs,
        CancellationToken cancellationToken)
    {
        var isControlBarVariant = contentMetadata.Category == GenPatcherContentCategory.ControlBar &&
                                  contentMetadata.SupportsVariants &&
                                  variant != null;

        try
        {
            var discovery = DiscoverManifestFiles(originalManifest, extractedDirectory, variant);
            if (discovery == null)
            {
                return null;
            }

            var (manifestDirectory, discoveredFiles) = discovery.Value;

            var targetGame = (variant != null && variant.TargetGame.HasValue)
                ? variant.TargetGame.Value
                : originalManifest.TargetGame;
            var dependencyBigFiles = CollectDependencyBigFiles(contentMetadata, targetGame);

            var alwaysIncludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (contentMetadata.Category == GenPatcherContentCategory.ControlBar)
            {
                alwaysIncludeFiles.Add("340_ControlBarProZH.big");
            }

            var controlBarContext = await ProcessControlBarOutputsAsync(
                isControlBarVariant,
                extractedDirectory,
                originalManifest,
                variant,
                allControlBarOutputs,
                discoveredFiles,
                cancellationToken);

            if (controlBarContext == null)
            {
                return null;
            }

            var (controlBarRepackedOutputs, allFiles) = controlBarContext.Value;

            var hasVariantBigFiles = variant != null && HasVariantBigFiles(
                allFiles,
                variant,
                controlBarRepackedOutputs,
                alwaysIncludeFiles,
                dependencyBigFiles);

            var inclusionContext = new ManifestInclusionContext(
                variant,
                isControlBarVariant,
                hasVariantBigFiles,
                dependencyBigFiles,
                alwaysIncludeFiles,
                controlBarRepackedOutputs);

            var fileEntries = await CollectManifestFilesAsync(
                allFiles,
                manifestDirectory,
                contentMetadata,
                inclusionContext,
                cancellationToken);

            var (manifestId, manifestName) = ResolveVariantIdentity(originalManifest, contentMetadata, variant, fileEntries.Count);
            return AssembleManifest(originalManifest, contentMetadata, variant, manifestId, manifestName, fileEntries);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build manifest for {Name}", originalManifest.Name);
            if (isControlBarVariant)
            {
                throw;
            }

            return null;
        }
    }

    /// <summary>
    /// Discovers the files to include in a manifest, resolving the manifest directory and
    /// preserving content outside of it (such as sibling subdirectories next to a language folder).
    /// </summary>
    /// <param name="originalManifest">The original manifest.</param>
    /// <param name="extractedDirectory">The extraction root directory.</param>
    /// <param name="variant">The content variant, or null for single-manifest content.</param>
    /// <returns>The manifest directory and discovered files, or null when no files were found.</returns>
    private (string ManifestDirectory, string[] AllFiles)? DiscoverManifestFiles(
        ContentManifest originalManifest,
        string extractedDirectory,
        ContentVariant? variant)
    {
        var manifestDirectory = variant == null
            ? GetManifestDirectory(originalManifest, extractedDirectory)
            : extractedDirectory;

        var fileList = new List<string>(Directory.GetFiles(manifestDirectory, "*.*", SearchOption.AllDirectories));
        if (variant == null && !string.Equals(manifestDirectory, extractedDirectory, StringComparison.OrdinalIgnoreCase) && Directory.Exists(extractedDirectory))
        {
            var siblingFiles = Directory.GetFiles(extractedDirectory, "*.*", SearchOption.AllDirectories)
                .Where(f => !PathHelper.IsPathWithinDirectory(manifestDirectory, f));
            fileList.AddRange(siblingFiles);
        }

        var allFiles = fileList.ToArray();
        if (allFiles.Length == 0)
        {
            logger.LogWarning("No files found in directory: {Directory}", manifestDirectory);
            return null;
        }

        logger.LogDebug("Found {FileCount} files in directory: {Directory}", allFiles.Length, manifestDirectory);
        return (manifestDirectory, allFiles);
    }

    /// <summary>
    /// Processes Control Bar variant repacking and resolves the effective file list.
    /// </summary>
    /// <param name="isControlBarVariant">Whether this is a Control Bar variant build.</param>
    /// <param name="extractedDirectory">The extraction root directory.</param>
    /// <param name="originalManifest">The original manifest.</param>
    /// <param name="variant">The content variant.</param>
    /// <param name="allControlBarOutputs">Accumulator for Control Bar outputs across variants.</param>
    /// <param name="discoveredFiles">The files discovered before Control Bar processing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The repacked outputs and effective files, or null when the variant should be skipped.</returns>
    private async Task<(HashSet<string> RepackedOutputs, string[] AllFiles)?> ProcessControlBarOutputsAsync(
        bool isControlBarVariant,
        string extractedDirectory,
        ContentManifest originalManifest,
        ContentVariant? variant,
        HashSet<string>? allControlBarOutputs,
        string[] discoveredFiles,
        CancellationToken cancellationToken)
    {
        if (!isControlBarVariant)
        {
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase), discoveredFiles);
        }

        var (shouldSkip, outputs) = await ProcessControlBarVariantAsync(
            extractedDirectory,
            originalManifest,
            variant,
            allControlBarOutputs,
            cancellationToken);

        if (shouldSkip)
        {
            return null;
        }

        return (outputs, Directory.GetFiles(extractedDirectory, "*.*", SearchOption.AllDirectories));
    }

    private async Task<(bool ShouldSkip, HashSet<string> Outputs)> ProcessControlBarVariantAsync(
        string extractedDirectory,
        ContentManifest originalManifest,
        ContentVariant? variant,
        HashSet<string>? allControlBarOutputs,
        CancellationToken cancellationToken)
    {
        var outputs = await controlBarProcessor.ProcessAndRepackControlBarAsync(
            extractedDirectory,
            originalManifest,
            variant?.Id,
            cleanupSources: false,
            cancellationToken);

        var repackedOutputs = new HashSet<string>(outputs, StringComparer.OrdinalIgnoreCase);
        if (repackedOutputs.Count == 0 ||
            repackedOutputs.All(controlBarProcessor.IsMetadataOnlyBig))
        {
            logger.LogInformation(
                "Skipping Control Bar variant {VariantId} because no matching variant assets were found in {Directory}",
                variant?.Id,
                extractedDirectory);
            return (true, repackedOutputs);
        }

        if (allControlBarOutputs != null)
        {
            foreach (var output in outputs)
            {
                allControlBarOutputs.Add(output);
            }
        }

        return (false, repackedOutputs);
    }

    private async Task<List<ManifestFile>> CollectManifestFilesAsync(
        string[] allFiles,
        string baseDirectory,
        GenPatcherContentMetadata contentMetadata,
        ManifestInclusionContext inclusionContext,
        CancellationToken cancellationToken)
    {
        var fileEntries = new List<ManifestFile>();
        foreach (var fullPath in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(baseDirectory, fullPath);
            if (relativePath.StartsWith("..", StringComparison.Ordinal))
            {
                relativePath = Path.GetFileName(fullPath);
            }

            if (!ShouldIncludeFile(relativePath, inclusionContext))
            {
                continue;
            }

            var hash = await hashProvider.ComputeFileHashAsync(fullPath, cancellationToken);
            var fileSize = new FileInfo(fullPath).Length;
            var isExecutable = relativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

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

        return fileEntries;
    }

    private (ManifestId ManifestId, string ManifestName) ResolveVariantIdentity(
        ContentManifest originalManifest,
        GenPatcherContentMetadata contentMetadata,
        ContentVariant? variant,
        int fileCount)
    {
        var manifestId = originalManifest.Id;
        var manifestName = originalManifest.Name;

        if (variant == null)
        {
            return (manifestId, manifestName);
        }

        var idParts = originalManifest.Id.Value.Split('.');
        if (idParts.Length >= 5)
        {
            var contentCode = contentMetadata.ContentCode;
            if (string.IsNullOrEmpty(contentCode))
            {
                contentCode = originalManifest.Metadata?.Tags?
                    .FirstOrDefault(t => t.StartsWith(ManifestTagConstants.ContentCodePrefix, StringComparison.OrdinalIgnoreCase))?[ManifestTagConstants.ContentCodePrefix.Length..];
            }

            if (string.IsNullOrEmpty(contentCode))
            {
                contentCode = idParts[4];
            }

            contentCode = GenPatcherContentRegistry.NormalizeContentCode(contentCode);

            var variantContentName = $"{contentCode}-{variant.Id}".ToLowerInvariant();
            manifestId = ManifestId.Create($"{idParts[0]}.{idParts[1]}.{idParts[2]}.{idParts[3]}.{variantContentName}");
        }

        var baseDisplayName = !string.IsNullOrWhiteSpace(contentMetadata.DisplayName)
            ? contentMetadata.DisplayName
            : originalManifest.Name;

        if (string.IsNullOrWhiteSpace(variant.Name))
        {
            manifestName = originalManifest.Name;
        }
        else if (variant.Name.Contains(baseDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            manifestName = variant.Name;
        }
        else
        {
            manifestName = $"{baseDisplayName} - {variant.Name}";
        }

        logger.LogInformation(
            "Creating variant manifest: {ManifestId} ({ManifestName}) with {FileCount} files",
            manifestId,
            manifestName,
            fileCount);

        return (manifestId, manifestName);
    }

    private ContentManifest AssembleManifest(
        ContentManifest originalManifest,
        GenPatcherContentMetadata contentMetadata,
        ContentVariant? variant,
        ManifestId manifestId,
        string manifestName,
        List<ManifestFile> fileEntries)
    {
        var variantTags = new List<string>();
        if (originalManifest.Metadata?.Tags != null)
        {
            variantTags.AddRange(originalManifest.Metadata.Tags.Where(t =>
                !t.StartsWith(ManifestTagConstants.SelectedVariantPrefix, StringComparison.OrdinalIgnoreCase) &&
                !t.StartsWith(ManifestTagConstants.RequestedVariantPrefix, StringComparison.OrdinalIgnoreCase) &&
                !t.StartsWith(ManifestTagConstants.VariantPrefix, StringComparison.OrdinalIgnoreCase) &&
                !t.StartsWith(ManifestTagConstants.ContentCodePrefix, StringComparison.OrdinalIgnoreCase)));
        }

        var normalizedCode = GenPatcherContentRegistry.NormalizeContentCode(contentMetadata.ContentCode);
        if (!string.IsNullOrEmpty(normalizedCode))
        {
            variantTags.Add($"{ManifestTagConstants.ContentCodePrefix}{normalizedCode}");
        }

        if (variant != null)
        {
            variantTags.Add($"{ManifestTagConstants.VariantPrefix}{variant.Id}");
            variantTags.Add($"{ManifestTagConstants.SelectedVariantPrefix}{variant.Id}");
        }

        var manifest = new ContentManifest
        {
            Id = manifestId,
            Name = manifestName,
            Version = originalManifest.Version,
            ManifestVersion = originalManifest.ManifestVersion,
            ContentType = originalManifest.ContentType,
            TargetGame = (variant != null && variant.TargetGame.HasValue) ? variant.TargetGame.Value : originalManifest.TargetGame,
            Files = fileEntries,
            EntryPoint = originalManifest.EntryPoint ?? contentMetadata.EntryPoint,
            Dependencies = [.. contentMetadata.GetDependencies().Where(d => d.InstallBehavior != DependencyInstallBehavior.AutoInstall)],
            InstallationInstructions = originalManifest.InstallationInstructions ?? new InstallationInstructions(),
            Publisher = originalManifest.Publisher,
            Metadata = new ContentMetadata
            {
                Description = originalManifest.Metadata?.Description ?? string.Empty,
                ReleaseDate = originalManifest.Metadata?.ReleaseDate ?? default,
                IconUrl = CommunityOutpostConstants.LogoSource,
                CoverUrl = CommunityOutpostConstants.CoverSource,
                ThemeColor = CommunityOutpostConstants.ThemeColor,
                ScreenshotUrls = originalManifest.Metadata?.ScreenshotUrls ?? [],
                Tags = variantTags,
                ChangelogUrl = originalManifest.Metadata?.ChangelogUrl,
                Variants = variant != null ? [] : (contentMetadata.Variants ?? []),
                RequiresVariantSelection = false,
                SelectedVariantId = variant?.Id,
            },
        };

        logger.LogInformation(
            "Built manifest {ManifestId} for {ContentType} '{Name}' with {FileCount} files and {DependencyCount} dependencies",
            manifest.Id,
            manifest.ContentType,
            manifest.Name,
            fileEntries.Count,
            manifest.Dependencies?.Count ?? 0);

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

    private bool ShouldIncludeFile(
        string relativePath,
        ManifestInclusionContext context)
    {
        var fileName = Path.GetFileName(relativePath);
        var normalizedPath = relativePath.Replace('\\', '/').ToLowerInvariant();
        var isDependencyBig = context.DependencyBigFiles.Contains(fileName);
        var isAlwaysInclude = context.AlwaysIncludeFiles.Contains(fileName);
        var isRepackedOutput = context.ControlBarRepackedOutputs.Contains(fileName);

        if (context.IsControlBarVariant && context.ControlBarRepackedOutputs.Count > 0 && !isRepackedOutput && !isDependencyBig && !isAlwaysInclude)
        {
            logger.LogDebug("Skipping file {File} because control bar variant is repacked into Art/Data BIG files", relativePath);
            return false;
        }

        if (context.IsControlBarVariant && context.ContainsVariantBigFiles && !fileName.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug("Skipping non-BIG file {File} for control bar variant {Variant}", relativePath, context.Variant?.Name);
            return false;
        }

        if (context.Variant != null)
        {
            if (context.Variant.IncludePatterns is { Count: > 0 })
            {
                bool matchesInclude = false;
                foreach (var pattern in context.Variant.IncludePatterns)
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
                    logger.LogDebug("Skipping file {File} - does not match variant {Variant} include patterns", relativePath, context.Variant.Name);
                    return false;
                }
            }

            if (context.Variant.ExcludePatterns is { Count: > 0 })
            {
                bool matchesExclude = false;
                foreach (var pattern in context.Variant.ExcludePatterns)
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
                    logger.LogDebug("Skipping file {File} - matches variant {Variant} exclude pattern", relativePath, context.Variant.Name);
                    return false;
                }
            }
        }

        return true;
    }

    private sealed record ManifestInclusionContext(
        ContentVariant? Variant,
        bool IsControlBarVariant,
        bool ContainsVariantBigFiles,
        HashSet<string> DependencyBigFiles,
        HashSet<string> AlwaysIncludeFiles,
        HashSet<string> ControlBarRepackedOutputs);
}
