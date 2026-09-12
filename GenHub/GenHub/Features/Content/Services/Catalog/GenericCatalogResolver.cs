using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Services.Dependencies;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Resolves a ContentSearchResult (from GenericCatalogDiscoverer) into a full ContentManifest.
/// </summary>
public partial class GenericCatalogResolver(
    ILogger<GenericCatalogResolver> logger,
    Func<IContentManifestBuilder> manifestBuilderFactory) : IContentResolver
{
    private readonly record struct ParsedVersionConstraint(
        string MinVersion,
        string MaxVersion,
        bool MinInclusive,
        bool MaxInclusive,
        List<string>? CompatibleVersions);

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
            var primaryArtifact = release.Artifacts?.FirstOrDefault(a => a.IsPrimary)
                ?? release.Artifacts?.FirstOrDefault();

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

            var artifactHashes = await RegisterRemoteFilesAsync(
                builder,
                release,
                contentItem,
                primaryArtifact);

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
                "application/x-rar-compressed" => ".rar",
                "application/x-7z-compressed" => ".7z",
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
    /// <returns>A sanitized filename, or <see cref="CatalogConstants.DefaultDownloadFilename"/> if the input is null or whitespace.</returns>
    private static string SanitizeFileName(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return CatalogConstants.DefaultDownloadFilename;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(filename.Select(c => invalidChars.Contains(c) ? '_' : c));
        return string.IsNullOrWhiteSpace(sanitized) ? CatalogConstants.DefaultDownloadFilename : sanitized;
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
                constraint);

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
        List<string>? compatibleVersions)
    {
        if (compatibleVersions is not { Count: > 0 } || string.IsNullOrEmpty(foundationMin))
        {
            return (null, compatibleVersions);
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

    private static string? AddCatalogDependency(
        IContentManifestBuilder builder,
        CatalogDependency dependency,
        ContentType initialDependencyType,
        CatalogContentItem contentItem,
        List<CatalogBundleComponentDescriptor>? bundleComponents,
        ParsedVersionConstraint constraint)
    {
        var (depPublisherId, depVersion, dependencyType) = ResolveDependencyIdentity(dependency, initialDependencyType, contentItem, bundleComponents);

        if (string.IsNullOrWhiteSpace(depPublisherId))
        {
            return $"Dependency '{dependency.ContentId}' has no publisher specified and host publisher could not be determined";
        }

        var dependencyId = CatalogManifestIdentity.CreateContentId(
            depPublisherId,
            dependencyType,
            dependency.ContentId,
            depVersion);

        var installBehavior = DependencyInstallBehavior.RequireExisting;
        if (dependency.IsOptional)
        {
            installBehavior = DependencyInstallBehavior.Optional;
        }
        else if (contentItem.ContentType == ContentType.ContentBundle)
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
            return boundsError;
        }

        builder.AddDependency(
            id: ManifestId.Create(dependencyId),
            name: dependency.ContentId,
            dependencyType: dependencyType,
            installBehavior: installBehavior,
            minVersion: constraint.MinVersion,
            maxVersion: constraint.MaxVersion,
            compatibleVersions: constraint.CompatibleVersions,
            isExclusive: false,
            conflictsWith: null,
            compatibleGameTypes: null,
            minInclusive: constraint.MinInclusive,
            maxInclusive: constraint.MaxInclusive);

        return null;
    }

    private static (string PublisherId, string Version, ContentType DependencyType) ResolveDependencyIdentity(
        CatalogDependency dependency,
        ContentType initialDependencyType,
        CatalogContentItem contentItem,
        List<CatalogBundleComponentDescriptor>? bundleComponents)
    {
        var cleanConstraint = CatalogManifestIdentity.StripVersionConstraint(dependency.VersionConstraint);
        var depPublisherId = !string.IsNullOrWhiteSpace(dependency.PublisherId)
            ? dependency.PublisherId
            : CatalogManifestIdentity.ResolveDeclaredPublisherType(contentItem);

        var depVersion = cleanConstraint;
        var dependencyType = initialDependencyType;

        if (bundleComponents?.FirstOrDefault(c => string.Equals(c.ContentId, dependency.ContentId, StringComparison.OrdinalIgnoreCase)) is { } matched)
        {
            if (!string.IsNullOrWhiteSpace(matched.PublisherId))
            {
                depPublisherId = matched.PublisherId;
            }

            if (!string.IsNullOrWhiteSpace(matched.ReleaseVersion))
            {
                depVersion = matched.ReleaseVersion;
            }

            if (!string.IsNullOrWhiteSpace(matched.ContentType) &&
                CatalogManifestIdentity.TryParseDeclaredContentType(matched.ContentType, out var matchedType))
            {
                dependencyType = matchedType;
            }
        }

        return (depPublisherId, depVersion, dependencyType);
    }

    [GeneratedRegex(@"([><=^~]+)\s+")]
    private static partial Regex OperatorWhitespaceRegex();

    private static ParsedVersionConstraint ParseVersionConstraint(string? constraint)
    {
        if (string.IsNullOrWhiteSpace(constraint))
        {
            return new(string.Empty, string.Empty, true, true, null);
        }

        var trimmed = OperatorWhitespaceRegex().Replace(constraint.Trim(), "$1");
        if (trimmed.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return new(string.Empty, string.Empty, true, true, null);
        }

        if (trimmed.Contains('|'))
        {
            return ParseListConstraint(trimmed);
        }

        if (trimmed.Contains(','))
        {
            if (trimmed.IndexOfAny(['>', '<', '^', '~']) >= 0)
            {
                return ParseRangedTokens(trimmed.Replace(',', ' '));
            }

            return ParseListConstraint(trimmed);
        }

        return ParseRangedTokens(trimmed);
    }

    private static ParsedVersionConstraint ParseListConstraint(string trimmed)
    {
        var parts = trimmed.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CatalogManifestIdentity.StripVersionConstraint)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new(string.Empty, string.Empty, true, true, parts.Count > 0 ? parts : null);
    }

    private static ParsedVersionConstraint ParseRangedTokens(string trimmed)
    {
        var tokens = trimmed.Split([' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 1 && CatalogManifestIdentity.TryParseExactVersion(tokens[0], out var exactVersion))
        {
            return new(exactVersion, exactVersion, true, true, [exactVersion]);
        }

        string minVersion = string.Empty;
        string maxVersion = string.Empty;
        var minInclusive = true;
        var maxInclusive = true;

        foreach (var token in tokens)
        {
            ApplyTokenBound(token, ref minVersion, ref maxVersion, ref minInclusive, ref maxInclusive);
        }

        return new(minVersion, maxVersion, minInclusive, maxInclusive, null);
    }

    private static void ApplyTokenBound(
        string token,
        ref string minVersion,
        ref string maxVersion,
        ref bool minInclusive,
        ref bool maxInclusive)
    {
        if (token.StartsWith(">=", StringComparison.Ordinal))
        {
            UpdateLowerBound(CatalogManifestIdentity.StripVersionConstraint(token), true, ref minVersion, ref minInclusive);
        }
        else if (token.StartsWith('>'))
        {
            UpdateLowerBound(CatalogManifestIdentity.StripVersionConstraint(token), false, ref minVersion, ref minInclusive);
        }
        else if (token.StartsWith("<=", StringComparison.Ordinal))
        {
            UpdateUpperBound(CatalogManifestIdentity.StripVersionConstraint(token), true, ref maxVersion, ref maxInclusive);
        }
        else if (token.StartsWith('<'))
        {
            UpdateUpperBound(CatalogManifestIdentity.StripVersionConstraint(token), false, ref maxVersion, ref maxInclusive);
        }
        else if (token.StartsWith('^'))
        {
            var target = CatalogManifestIdentity.StripVersionConstraint(token);
            UpdateLowerBound(target, true, ref minVersion, ref minInclusive);
            var parts = target.Split('.');
            if (parts.Length > 0 && int.TryParse(parts[0], out var major))
            {
                UpdateUpperBound($"{major + 1}.0.0", false, ref maxVersion, ref maxInclusive);
            }
        }
        else if (token.StartsWith('~'))
        {
            var target = CatalogManifestIdentity.StripVersionConstraint(token);
            UpdateLowerBound(target, true, ref minVersion, ref minInclusive);
            var parts = target.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor))
            {
                UpdateUpperBound($"{major}.{minor + 1}.0", false, ref maxVersion, ref maxInclusive);
            }
        }
    }

    private static void UpdateLowerBound(
        string candidateMin,
        bool candidateInclusive,
        ref string currentMin,
        ref bool currentInclusive)
    {
        if (string.IsNullOrEmpty(candidateMin))
        {
            return;
        }

        if (string.IsNullOrEmpty(currentMin))
        {
            currentMin = candidateMin;
            currentInclusive = candidateInclusive;
            return;
        }

        var cmp = CatalogManifestIdentity.CompareVersions(candidateMin, currentMin);
        if (cmp > 0)
        {
            currentMin = candidateMin;
            currentInclusive = candidateInclusive;
        }
        else if (cmp == 0)
        {
            currentInclusive = currentInclusive && candidateInclusive;
        }
    }

    private static void UpdateUpperBound(
        string candidateMax,
        bool candidateInclusive,
        ref string currentMax,
        ref bool currentInclusive)
    {
        if (string.IsNullOrEmpty(candidateMax))
        {
            return;
        }

        if (string.IsNullOrEmpty(currentMax))
        {
            currentMax = candidateMax;
            currentInclusive = candidateInclusive;
            return;
        }

        var cmp = CatalogManifestIdentity.CompareVersions(candidateMax, currentMax);
        if (cmp < 0)
        {
            currentMax = candidateMax;
            currentInclusive = candidateInclusive;
        }
        else if (cmp == 0)
        {
            currentInclusive = currentInclusive && candidateInclusive;
        }
    }

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
            var variantTag = $"variant:{primaryArtifact.Variant.ToLowerInvariant()}";
            if (!manifest.Metadata.Tags.Contains(variantTag, StringComparer.OrdinalIgnoreCase))
            {
                manifest.Metadata.Tags.Add(variantTag);
            }
        }

        if (manifest.Metadata.Tags.All(t => !t.StartsWith("contentCode:", StringComparison.OrdinalIgnoreCase)))
        {
            manifest.Metadata.Tags.Add($"contentCode:{contentItem.Id}");
        }
    }

    private async Task<Dictionary<string, string>> RegisterRemoteFilesAsync(
        IContentManifestBuilder builder,
        ContentRelease release,
        CatalogContentItem contentItem,
        ReleaseArtifact? primaryArtifact)
    {
        var artifactHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (release.Artifacts is { Count: > 0 })
        {
            var usedFilenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var artifact in release.Artifacts)
            {
                if (string.IsNullOrWhiteSpace(artifact.DownloadUrl))
                {
                    continue;
                }

                // If primary artifact belongs to a specific variant on an axis, register it plus
                // common artifacts: no variant axis, a different axis, or no variant label on
                // the same axis. Same-axis artifacts with a different variant are excluded.
                if (!string.IsNullOrWhiteSpace(primaryArtifact?.VariantAxis) &&
                    !string.IsNullOrWhiteSpace(artifact.VariantAxis) &&
                    string.Equals(primaryArtifact.VariantAxis, artifact.VariantAxis, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(artifact.Variant) &&
                    !string.Equals(primaryArtifact.Variant, artifact.Variant, StringComparison.OrdinalIgnoreCase))
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
                    isExecutable: false,
                    permissions: null);
            }
        }
        else
        {
            logger.LogInformation(
                "Content '{ContentName}' has no downloadable artifacts (dependency-only package)",
                contentItem.Name);
        }

        return artifactHashes;
    }
}
