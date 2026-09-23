using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Services.Dependencies;
using GenHub.Core.Utilities;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Resolves a ContentSearchResult (from GenericCatalogDiscoverer) into a full ContentManifest.
/// </summary>
public partial class GenericCatalogResolver(
    ILogger<GenericCatalogResolver> logger,
    Func<IContentManifestBuilder> manifestBuilderFactory,
    ILocalizationService? localizationService = null) : IContentResolver
{
    private readonly record struct ManifestResolutionContext(
        string DeclaredPublisherId,
        string ResolvedName,
        string? SearchResultId,
        GameType ResolvedTargetGame);

    /// <inheritdoc />
    public string ResolverId => CatalogConstants.GenericCatalogResolverId;

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discoveredItem);

        try
        {
            // Extract catalog item and release metadata
            if (!discoveredItem.ResolverMetadata.TryGetValue(CatalogConstants.ReleaseJsonMetadataKey, out var releaseJson))
            {
                return OperationResult<ContentManifest>.CreateFailure("Missing release metadata");
            }

            if (!discoveredItem.ResolverMetadata.TryGetValue(CatalogConstants.CatalogItemJsonMetadataKey, out var contentItemJson))
            {
                return OperationResult<ContentManifest>.CreateFailure("Missing content item metadata");
            }

            if (!discoveredItem.ResolverMetadata.TryGetValue(CatalogConstants.PublisherProfileJsonMetadataKey, out var publisherJson))
            {
                return OperationResult<ContentManifest>.CreateFailure("Missing publisher profile");
            }

            // Deserialize from JSON
            var release = JsonSerializer.Deserialize<ContentRelease>(releaseJson);
            var contentItem = JsonSerializer.Deserialize<CatalogContentItem>(contentItemJson);
            var publisher = JsonSerializer.Deserialize<PublisherProfile>(publisherJson);

            if (release == null || contentItem == null || publisher == null)
            {
                return OperationResult<ContentManifest>.CreateFailure("Failed to deserialize catalog metadata");
            }

            logger.LogInformation(
                "Resolving content '{ContentName}' v{Version} from publisher '{PublisherId}'",
                contentItem.Name,
                release.Version,
                publisher.Id);

            var declaredPublisherId = CatalogManifestIdentity.ResolveDeclaredPublisherType(contentItem);

            var publisherDisplayName = !string.IsNullOrWhiteSpace(contentItem.Metadata?.Author)
                ? contentItem.Metadata.Author
                : publisher.Name;

            var website = !string.IsNullOrWhiteSpace(contentItem.Metadata?.DocumentationUrl)
                ? contentItem.Metadata.DocumentationUrl
                : (publisher.Website ?? string.Empty);

            // ContentBundle (and other meta-packages) may ship no downloadable artifacts —
            // their payload is the dependency graph alone. Skip remote-file registration.
            // The primary is selected from installable artifacts only: a rejected primary
            // (disk image, system package) must not drive naming or exclude usable variants.
            var (prePartitionedUsable, _) = PartitionInstallableArtifacts(release, contentItem, localizationService);
            var primaryArtifact = prePartitionedUsable.FirstOrDefault(a => a.IsPrimary)
                ?? prePartitionedUsable.FirstOrDefault();

            var effectiveContentId = !string.IsNullOrWhiteSpace(primaryArtifact?.Variant)
                ? $"{contentItem.Id}-{primaryArtifact.Variant.Trim()}"
                : contentItem.Id;

            var resolvedName = ResolveManifestName(discoveredItem, contentItem, primaryArtifact);
            var resolvedTargetGame = ResolveTargetGame(contentItem, primaryArtifact, discoveredItem);

            var builder = manifestBuilderFactory()
                .WithBasicInfo(declaredPublisherId, effectiveContentId, release.Version)
                .WithContentType(contentItem.ContentType, resolvedTargetGame)
                .WithName(resolvedName)
                .WithPublisher(
                    publisherDisplayName,
                    website,
                    publisher.SupportUrl ?? string.Empty,
                    publisher.ContactEmail ?? string.Empty,
                    publisherType: declaredPublisherId)
                .WithMetadata(
                    description: contentItem.Description,
                    tags: [.. contentItem.Tags],
                    iconUrl: contentItem.Metadata?.BannerUrl ?? string.Empty,
                    screenshotUrls: contentItem.Metadata?.ScreenshotUrls?.ToList(),
                    changelogUrl: contentItem.Metadata?.DocumentationUrl ?? string.Empty);

            var remoteFilesResult = await RegisterRemoteFilesAsync(
                builder,
                release,
                contentItem,
                primaryArtifact);
            if (!remoteFilesResult.Success || remoteFilesResult.Data is null)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    remoteFilesResult.FirstError ?? "No installable artifacts in release.");
            }

            var artifactHashes = remoteFilesResult.Data;

            var dependencyError = AddDependencies(logger, builder, discoveredItem, release, contentItem, resolvedTargetGame);
            if (dependencyError != null)
            {
                logger.LogWarning(
                    "Failed to reconcile dependencies for content '{ContentName}': {Error}",
                    contentItem.Name,
                    dependencyError);
                return OperationResult<ContentManifest>.CreateFailure(dependencyError);
            }

            var manifest = builder.Build();

            ApplyManifestPostProcessing(
                manifest,
                contentItem,
                primaryArtifact,
                new ManifestResolutionContext(
                    declaredPublisherId,
                    resolvedName,
                    discoveredItem.Id,
                    resolvedTargetGame),
                artifactHashes);

            logger.LogInformation(
                "Successfully resolved manifest for '{ContentName}' with {FileCount} files",
                manifest.Name,
                manifest.Files.Count);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve content from catalog");
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }

    private static string ResolveManifestName(
        ContentSearchResult searchResult,
        CatalogContentItem contentItem,
        ReleaseArtifact? primaryArtifact)
    {
        if (!string.IsNullOrWhiteSpace(searchResult.Name))
        {
            return searchResult.Name;
        }

        return !string.IsNullOrWhiteSpace(primaryArtifact?.Variant)
            ? $"{contentItem.Name} ({primaryArtifact.Variant})"
            : contentItem.Name;
    }

    private static GameType ResolveTargetGame(
        CatalogContentItem contentItem,
        ReleaseArtifact? primaryArtifact,
        ContentSearchResult searchResult)
    {
        if (primaryArtifact?.VariantAxis?.Equals(CatalogConstants.GameTypeVariantAxis, StringComparison.OrdinalIgnoreCase) == true)
        {
            if (primaryArtifact.Variant?.Equals(CatalogConstants.GeneralsVariantLabel, StringComparison.OrdinalIgnoreCase) == true)
            {
                return GameType.Generals;
            }

            if (primaryArtifact.Variant?.Equals(CatalogConstants.ZeroHourVariantLabel, StringComparison.OrdinalIgnoreCase) == true ||
                primaryArtifact.Variant?.Equals(CatalogConstants.ZeroHourCompactVariantLabel, StringComparison.OrdinalIgnoreCase) == true)
            {
                return GameType.ZeroHour;
            }
        }

        if (searchResult.TargetGame != GameType.Unknown)
        {
            return searchResult.TargetGame;
        }

        return contentItem.TargetGame;
    }

    private static string SanitizeArtifactFilename(ReleaseArtifact primaryArtifact, CatalogContentItem contentItem)
    {
        var filename = primaryArtifact.Filename;
        if (string.IsNullOrEmpty(filename) ||
            filename.Contains("fetch.aspx", StringComparison.OrdinalIgnoreCase) ||
            filename.Contains('?') ||
            filename.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var rawUrl = primaryArtifact.DownloadUrl;
            var queryIndex = rawUrl.IndexOfAny(['?', '#']);
            var cleanUrl = queryIndex >= 0 ? rawUrl[..queryIndex] : rawUrl;
            var extension = primaryArtifact.ContentType?.ToLowerInvariant() switch
            {
                "application/zip" => ".zip",
                "application/x-rar-compressed" or "application/vnd.rar" or "application/x-rar" or "application/rar" => ".rar",
                "application/x-7z-compressed" => ".7z",
                "application/x-tar" => ".tar",
                "application/gzip" => ".gz",
                _ => Path.GetExtension(cleanUrl) is { Length: > 1 } urlExt
                    ? urlExt
                    : ".zip",
            };
            return SanitizeFileName($"{Path.GetFileName(contentItem.Name)}{extension}");
        }

        return SanitizeFileName(Path.GetFileName(filename));
    }

    private static string DisambiguateFilename(HashSet<string> usedFilenames, string baseFilename)
    {
        var filename = baseFilename;
        var counter = 1;
        var stem = Path.GetFileNameWithoutExtension(baseFilename);
        var ext = Path.GetExtension(baseFilename);

        while (usedFilenames.Contains(filename))
        {
            filename = $"{stem}_{counter}{ext}";
            counter++;
        }

        return filename;
    }

    /// <summary>
    /// Sanitizes a filename by replacing invalid filesystem characters with underscores.
    /// </summary>
    /// <param name="filename">The filename to sanitize.</param>
    /// <returns>A sanitized filename, or <see cref="CatalogConstants.DefaultDownloadFilename"/> if the input is null, whitespace, or empty.</returns>
    private static string SanitizeFileName(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return CatalogConstants.DefaultDownloadFilename;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(filename.Select(c => invalidChars.Contains(c) ? '_' : c));
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized.All(c => c == '_' || c == '.' || char.IsWhiteSpace(c)))
        {
            return CatalogConstants.DefaultDownloadFilename;
        }

        return sanitized;
    }

    private static string? AddDependencies(
        ILogger logger,
        IContentManifestBuilder builder,
        ContentSearchResult discoveredItem,
        ContentRelease release,
        CatalogContentItem contentItem,
        GameType resolvedTargetGame)
    {
        var (bundleComponents, deserializeError) = TryDeserializeBundleComponents(logger, discoveredItem, contentItem.Id);
        if (deserializeError != null)
        {
            return deserializeError;
        }

        if (release.Dependencies == null || release.Dependencies.Count == 0)
        {
            return null;
        }

        foreach (var dependency in release.Dependencies)
        {
            var dependencyType = CatalogManifestIdentity.ResolveDependencyContentType(dependency, contentItem);
            var constraint = ParseVersionConstraint(dependency.VersionConstraint);

            if (dependencyType == ContentType.GameInstallation ||
                CatalogManifestIdentity.IsBaseGameDependency(dependency))
            {
                var error = AddBaseGameDependency(
                    builder,
                    dependency,
                    resolvedTargetGame,
                    constraint);

                if (error != null)
                {
                    return error;
                }

                continue;
            }

            var catError = AddCatalogDependency(
                builder,
                dependency,
                dependencyType,
                contentItem,
                bundleComponents,
                constraint,
                logger);

            if (catError != null)
            {
                return catError;
            }
        }

        return null;
    }

    private static (List<CatalogBundleComponentDescriptor>? Components, string? Error) TryDeserializeBundleComponents(
        ILogger logger,
        ContentSearchResult discoveredItem,
        string contentItemId)
    {
        if (!discoveredItem.ResolverMetadata.TryGetValue(CatalogConstants.BundleComponentsJsonMetadataKey, out var bundleJson) ||
            string.IsNullOrWhiteSpace(bundleJson))
        {
            return (null, null);
        }

        try
        {
            var components = JsonSerializer.Deserialize<List<CatalogBundleComponentDescriptor>>(bundleJson);
            if (components == null)
            {
                logger.LogWarning("Bundle component metadata for '{ContentId}' deserialized to null", contentItemId);
                return (null, $"Bundle component metadata for '{contentItemId}' is invalid.");
            }

            return (components, null);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to deserialize bundle component metadata for '{ContentId}'", contentItemId);
            return (null, $"Bundle component metadata for '{contentItemId}' is invalid: {ex.Message}");
        }
    }

    private static string? AddBaseGameDependency(
        IContentManifestBuilder builder,
        CatalogDependency dependency,
        GameType resolvedTargetGame,
        ParsedVersionConstraint constraint)
    {
        var isGenerals = dependency.ContentId.Equals(CatalogConstants.GeneralsContentId, StringComparison.OrdinalIgnoreCase) ||
                         resolvedTargetGame == GameType.Generals;
        var foundation = isGenerals &&
                         !dependency.ContentId.Equals(CatalogConstants.ZeroHourContentId, StringComparison.OrdinalIgnoreCase)
            ? BaseDependencyBuilder.CreateGenerals108Dependency()
            : BaseDependencyBuilder.CreateZeroHour104Dependency();

        var foundationMin = foundation.MinVersion ?? string.Empty;

        var (compatibleError, effectiveCompatibleVersions) = ReconcileCompatibleVersionsFloor(
            dependency.ContentId,
            foundationMin,
            constraint.CompatibleVersions);

        if (compatibleError != null)
        {
            return compatibleError;
        }

        var (effectiveMinVersion, effectiveMinInclusive) = ComputeEffectiveBaseGameMinVersion(
            foundationMin,
            constraint);

        var isReconciled = !string.Equals(effectiveMinVersion, constraint.MinVersion, StringComparison.OrdinalIgnoreCase) ||
                           effectiveMinInclusive != constraint.MinInclusive;

        var boundsError = ValidateVersionBounds(
            dependency.ContentId,
            effectiveMinVersion,
            constraint.MaxVersion,
            effectiveMinInclusive,
            constraint.MaxInclusive,
            isReconciled: isReconciled);

        if (boundsError != null)
        {
            return boundsError;
        }

        builder.AddDependency(
            id: foundation.Id,
            name: foundation.Name,
            dependencyType: ContentType.GameInstallation,
            installBehavior: DependencyInstallBehavior.RequireExisting,
            minVersion: effectiveMinVersion,
            maxVersion: constraint.MaxVersion,
            compatibleVersions: effectiveCompatibleVersions,
            isExclusive: false,
            conflictsWith: null,
            compatibleGameTypes: foundation.CompatibleGameTypes,
            minInclusive: effectiveMinInclusive,
            maxInclusive: constraint.MaxInclusive);

        return null;
    }

    private static (string? Error, List<string>? ReconciledVersions) ReconcileCompatibleVersionsFloor(
        string contentId,
        string foundationMin,
        IReadOnlyList<string>? compatibleVersions)
    {
        if (compatibleVersions is not { Count: > 0 } || string.IsNullOrEmpty(foundationMin))
        {
            return (null, compatibleVersions != null ? [.. compatibleVersions] : null);
        }

        var filtered = compatibleVersions
            .Where(v => CatalogManifestIdentity.CompareVersions(v, foundationMin) >= 0)
            .ToList();

        if (filtered.Count == 0)
        {
            return (
                $"Dependency '{contentId}' has unsatisfiable version bounds after reconciliation: all compatible versions are below the minimum foundation floor '{foundationMin}'.",
                null);
        }

        return (null, filtered);
    }

    private static (string EffectiveMinVersion, bool EffectiveMinInclusive) ComputeEffectiveBaseGameMinVersion(
        string foundationMinVersion,
        ParsedVersionConstraint constraint)
    {
        var effectiveMinVersion = foundationMinVersion;
        var effectiveMinInclusive = true;

        if (!string.IsNullOrEmpty(constraint.MinVersion))
        {
            if (string.IsNullOrEmpty(effectiveMinVersion) ||
                CatalogManifestIdentity.CompareVersions(constraint.MinVersion, effectiveMinVersion) > 0)
            {
                effectiveMinVersion = constraint.MinVersion;
                effectiveMinInclusive = constraint.MinInclusive;
            }
            else if (CatalogManifestIdentity.CompareVersions(constraint.MinVersion, effectiveMinVersion) == 0)
            {
                effectiveMinInclusive = constraint.MinInclusive;
            }
        }
        else if (constraint.CompatibleVersions is { Count: > 0 })
        {
            effectiveMinVersion = string.Empty;
        }

        return (effectiveMinVersion, effectiveMinInclusive);
    }

    private static string? ValidateVersionBounds(
        string contentId,
        string minVersion,
        string maxVersion,
        bool minInclusive,
        bool maxInclusive,
        bool isReconciled = false)
    {
        if (string.IsNullOrEmpty(minVersion) || string.IsNullOrEmpty(maxVersion))
        {
            return null;
        }

        var comparison = CatalogManifestIdentity.CompareVersions(maxVersion, minVersion);
        var context = isReconciled ? " after reconciliation" : string.Empty;

        if (comparison < 0)
        {
            return $"Dependency '{contentId}' has unsatisfiable version bounds{context}: min '{minVersion}' > max '{maxVersion}'.";
        }

        if (comparison == 0 && (!minInclusive || !maxInclusive))
        {
            return $"Dependency '{contentId}' has unsatisfiable version bounds{context}: min '{minVersion}' and max '{maxVersion}' produce an empty range.";
        }

        return null;
    }

    private static CatalogBundleComponentDescriptor? FindMatchingBundleComponent(
        List<CatalogBundleComponentDescriptor>? bundleComponents,
        CatalogDependency dependency)
    {
        if (bundleComponents == null || bundleComponents.Count == 0)
        {
            return null;
        }

        var isBaseGame = CatalogManifestIdentity.IsBaseGameDependency(dependency);

        return bundleComponents.FirstOrDefault(c =>
            string.Equals(c.ContentId, dependency.ContentId, StringComparison.OrdinalIgnoreCase) &&
            c.IsBaseGame == isBaseGame &&
            (string.IsNullOrWhiteSpace(dependency.PublisherId) ||
             string.Equals(c.PublisherId, CatalogManifestIdentity.ResolveDeclaredPublisherType(dependency.PublisherId), StringComparison.OrdinalIgnoreCase)));
    }

    private static string? AddCatalogDependency(
        IContentManifestBuilder builder,
        CatalogDependency dependency,
        ContentType initialDependencyType,
        CatalogContentItem contentItem,
        List<CatalogBundleComponentDescriptor>? bundleComponents,
        ParsedVersionConstraint constraint,
        ILogger logger)
    {
        var (depPublisherId, depVersion, dependencyType) = ResolveDependencyIdentity(dependency, initialDependencyType, contentItem, bundleComponents, logger);

        if (string.IsNullOrWhiteSpace(depPublisherId))
        {
            return $"Dependency '{dependency.ContentId}' has no publisher specified and host publisher could not be determined";
        }

        var matchedComponent = FindMatchingBundleComponent(bundleComponents, dependency);
        var defaultVariant = matchedComponent?.Variants.FirstOrDefault(v => v.IsDefault) ?? matchedComponent?.Variants.FirstOrDefault();

        var dependencyId = defaultVariant != null && !string.IsNullOrWhiteSpace(defaultVariant.CatalogId)
            ? defaultVariant.CatalogId
            : CatalogManifestIdentity.CreateContentId(
                depPublisherId,
                dependencyType,
                dependency.ContentId,
                depVersion);

        var installBehavior = DependencyInstallBehavior.RequireExisting;
        if (dependency.IsOptional)
        {
            installBehavior = DependencyInstallBehavior.Optional;
        }
        else if (contentItem.ContentType == ContentType.ContentBundle ||
                 (matchedComponent is { IsAvailable: true } && matchedComponent.Variants.Count > 0))
        {
            installBehavior = DependencyInstallBehavior.AutoInstall;
        }

        var boundsError = ValidateVersionBounds(
            dependency.ContentId,
            constraint.MinVersion,
            constraint.MaxVersion,
            constraint.MinInclusive,
            constraint.MaxInclusive,
            isReconciled: false);

        if (boundsError != null)
        {
            if (dependency.IsOptional)
            {
                logger.LogWarning("Skipping optional dependency '{ContentId}' with unsatisfiable version bounds: {Error}", dependency.ContentId, boundsError);
                return null;
            }

            return boundsError;
        }

        builder.AddDependency(
            id: ManifestId.Create(dependencyId),
            name: dependency.ContentId,
            dependencyType: dependencyType,
            installBehavior: installBehavior,
            minVersion: constraint.MinVersion,
            maxVersion: constraint.MaxVersion,
            compatibleVersions: constraint.CompatibleVersions != null ? [.. constraint.CompatibleVersions] : null,
            isExclusive: false,
            conflictsWith: null,
            compatibleGameTypes: null,
            minInclusive: constraint.MinInclusive,
            maxInclusive: constraint.MaxInclusive,
            strictPublisher: true,
            publisherType: depPublisherId);

        return null;
    }

    private static (string PublisherId, string Version, ContentType DependencyType) ResolveDependencyIdentity(
        CatalogDependency dependency,
        ContentType initialDependencyType,
        CatalogContentItem contentItem,
        List<CatalogBundleComponentDescriptor>? bundleComponents,
        ILogger logger)
    {
        var cleanConstraint = CatalogManifestIdentity.StripVersionConstraint(dependency.VersionConstraint);
        var depPublisherId = !string.IsNullOrWhiteSpace(dependency.PublisherId)
            ? CatalogManifestIdentity.ResolveDeclaredPublisherType(dependency.PublisherId)
            : CatalogManifestIdentity.ResolveDeclaredPublisherType(contentItem);

        var depVersion = cleanConstraint;
        var dependencyType = initialDependencyType;

        if (FindMatchingBundleComponent(bundleComponents, dependency) is { } matched)
        {
            if (!string.IsNullOrWhiteSpace(matched.PublisherId))
            {
                depPublisherId = CatalogManifestIdentity.ResolveDeclaredPublisherType(matched.PublisherId);
            }

            if (!string.IsNullOrWhiteSpace(matched.ReleaseVersion))
            {
                depVersion = matched.ReleaseVersion;
            }

            dependencyType = ResolveMatchedComponentType(matched, initialDependencyType, dependency.ContentId, logger);
        }

        return (depPublisherId, depVersion, dependencyType);
    }

    private static ContentType ResolveMatchedComponentType(
        CatalogBundleComponentDescriptor matched,
        ContentType fallbackType,
        string contentId,
        ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(matched.CatalogItemJson))
        {
            try
            {
                var sibling = JsonSerializer.Deserialize<CatalogContentItem>(matched.CatalogItemJson);
                if (sibling != null && Enum.IsDefined(sibling.ContentType))
                {
                    return sibling.ContentType;
                }
            }
            catch (JsonException ex)
            {
                logger.LogDebug(ex, "Failed to deserialize sibling catalog item JSON for dependency '{DependencyId}'", contentId);
            }
        }

        if (!string.IsNullOrWhiteSpace(matched.ContentType) &&
            CatalogManifestIdentity.TryParseDeclaredContentType(matched.ContentType, out var matchedType))
        {
            return matchedType;
        }

        return fallbackType;
    }

    private static ParsedVersionConstraint ParseVersionConstraint(string? constraint) =>
        CatalogManifestIdentity.ParseVersionConstraint(constraint);

    private static void ApplyFileHashes(
        ContentManifest manifest,
        CatalogContentItem contentItem,
        ReleaseArtifact? primaryArtifact,
        IReadOnlyDictionary<string, string>? artifactHashes)
    {
        if (artifactHashes is { Count: > 0 })
        {
            foreach (var file in manifest.Files)
            {
                if (artifactHashes.TryGetValue(file.RelativePath, out var hash) && !string.IsNullOrWhiteSpace(hash))
                {
                    file.Hash = hash;
                }
            }
        }
        else if (primaryArtifact != null && !string.IsNullOrWhiteSpace(primaryArtifact.Sha256))
        {
            var primaryFilename = SanitizeArtifactFilename(primaryArtifact, contentItem);
            var primaryFile = manifest.Files.FirstOrDefault(f => string.Equals(f.RelativePath, primaryFilename, StringComparison.OrdinalIgnoreCase));
            if (primaryFile != null)
            {
                primaryFile.Hash = primaryArtifact.Sha256;
            }
        }
    }

    private static void ApplyManifestPostProcessing(
        ContentManifest manifest,
        CatalogContentItem contentItem,
        ReleaseArtifact? primaryArtifact,
        ManifestResolutionContext context,
        IReadOnlyDictionary<string, string>? artifactHashes = null)
    {
        ApplyFileHashes(manifest, contentItem, primaryArtifact, artifactHashes);

        if (!string.IsNullOrWhiteSpace(context.SearchResultId) &&
            ManifestIdValidator.IsValid(context.SearchResultId, out _))
        {
            manifest.Id = ManifestId.Create(context.SearchResultId);
        }

        manifest.Name = context.ResolvedName;
        manifest.OriginalProviderName = context.DeclaredPublisherId;
        manifest.OriginalContentId = context.SearchResultId ?? contentItem.Id;

        foreach (var dep in manifest.Dependencies)
        {
            if (dep.DependencyType == ContentType.GameInstallation && dep.CompatibleGameTypes.Count == 0)
            {
                dep.CompatibleGameTypes.Add(context.ResolvedTargetGame);
            }
        }

        manifest.Metadata.Description = contentItem.Description;
        manifest.Metadata.Tags = [.. contentItem.Tags];

        if (!string.IsNullOrWhiteSpace(primaryArtifact?.Variant))
        {
            var variantTag = $"{ManifestTagConstants.VariantPrefix}{primaryArtifact.Variant.ToLowerInvariant()}";
            if (!manifest.Metadata.Tags.Contains(variantTag, StringComparer.OrdinalIgnoreCase))
            {
                manifest.Metadata.Tags.Add(variantTag);
            }
        }

        if (manifest.Metadata.Tags.All(t => !t.StartsWith(ManifestTagConstants.ContentCodePrefix, StringComparison.OrdinalIgnoreCase)))
        {
            manifest.Metadata.Tags.Add($"{ManifestTagConstants.ContentCodePrefix}{contentItem.Id}");
        }
    }

    /// <summary>
    /// Splits release artifacts into installable content and guided rejections, so a disk
    /// image or container package can never become a manifest file.
    /// </summary>
    private static (IReadOnlyList<ReleaseArtifact> Usable, IReadOnlyList<string> Rejections) PartitionInstallableArtifacts(ContentRelease release, CatalogContentItem contentItem, ILocalizationService? localizationService = null)
    {
        var usable = new List<ReleaseArtifact>();
        var rejections = new List<string>();
        foreach (var artifact in release.Artifacts ?? [])
        {
            // Classify both spellings: a sanitized GUID filename must not launder a real
            // disk image or installer past the policy.
            var rejection = ContentFormatPolicy.GetRejectionMessage(artifact.Filename, localizationService)
                ?? ContentFormatPolicy.GetRejectionMessage(SanitizeArtifactFilename(artifact, contentItem), localizationService);
            if (rejection is null)
            {
                usable.Add(artifact);
            }
            else
            {
                rejections.Add(rejection);
            }
        }

        return (usable, rejections);
    }

    private static bool ShouldSkipVariant(ReleaseArtifact? primaryArtifact, ReleaseArtifact artifact)
    {
        // If primary artifact belongs to a specific variant on an axis, register it plus
        // common artifacts: no variant axis, a different axis, or no variant label on
        // the same axis. Same-axis artifacts with a different variant are excluded.
        if (string.IsNullOrWhiteSpace(primaryArtifact?.VariantAxis) ||
            string.IsNullOrWhiteSpace(artifact.VariantAxis))
        {
            return false;
        }

        return string.Equals(primaryArtifact.VariantAxis, artifact.VariantAxis, StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(artifact.Variant) &&
               !string.Equals(primaryArtifact.Variant, artifact.Variant, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<OperationResult<Dictionary<string, string>>> RegisterRemoteFilesAsync(
        IContentManifestBuilder builder,
        ContentRelease release,
        CatalogContentItem contentItem,
        ReleaseArtifact? primaryArtifact)
    {
        var artifactHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (release.Artifacts is not { Count: > 0 })
        {
            logger.LogInformation(
                "Content '{ContentName}' has no downloadable artifacts (dependency-only package)",
                contentItem.Name);
            return OperationResult<Dictionary<string, string>>.CreateSuccess(artifactHashes);
        }

        var (usableArtifacts, rejections) = PartitionInstallableArtifacts(release, contentItem, localizationService);
        foreach (var rejection in rejections)
        {
            logger.LogWarning("Skipping uninstallable artifact for '{ContentName}': {Rejection}", contentItem.Name, rejection);
        }

        if (usableArtifacts.Count == 0)
        {
            return OperationResult<Dictionary<string, string>>.CreateFailure(
                $"Release {release.Version} of '{contentItem.Name}' has no installable artifacts. {string.Join(" ", rejections)}");
        }

        var usedFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registeredCount = 0;

        foreach (var artifact in usableArtifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.DownloadUrl) || ShouldSkipVariant(primaryArtifact, artifact))
            {
                continue;
            }

            var baseFilename = SanitizeArtifactFilename(artifact, contentItem);
            var filename = DisambiguateFilename(usedFilenames, baseFilename);
            usedFilenames.Add(filename);

            if (!string.IsNullOrWhiteSpace(artifact.Sha256))
            {
                artifactHashes[filename] = artifact.Sha256;
            }

            logger.LogDebug(
                "Adding remote file {Filename} with download URL {Url}",
                filename,
                artifact.DownloadUrl);

            await builder.AddRemoteFileAsync(
                relativePath: filename,
                downloadUrl: artifact.DownloadUrl,
                sourceType: ContentSourceType.RemoteDownload,
                isExecutable: GitHubInferenceHelper.IsExecutableFile(filename),
                permissions: null);
            registeredCount++;
        }

        if (registeredCount == 0)
        {
            return OperationResult<Dictionary<string, string>>.CreateFailure(
                $"Release {release.Version} of '{contentItem.Name}' declares {usableArtifacts.Count} installable artifact(s) but none could be registered (missing download URLs or variant filtering excluded them).");
        }

        return OperationResult<Dictionary<string, string>>.CreateSuccess(artifactHashes);
    }
}
