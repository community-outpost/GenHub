using System;
using System.IO;
using System.Linq;
using System.Text.Json;
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
/// Resolves GenHub-schema catalog items into <see cref="ContentManifest"/>s for install.
/// </summary>
/// <remarks>
/// Paired with <see cref="GenericCatalogDiscoverer"/> for any subscribed catalog — the modular
/// path that avoids per-publisher resolvers. Uses <see cref="IContentManifestBuilder"/> for
/// download, archive extraction, and CAS registration.
/// </remarks>
public class GenericCatalogResolver(
    ILogger<GenericCatalogResolver> logger,
    Func<IContentManifestBuilder> manifestBuilderFactory) : IContentResolver
{
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
            // Extract catalog metadata from search result (stored as JSON strings)
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

            if (primaryArtifact != null)
            {
                var filename = SanitizeArtifactFilename(primaryArtifact, contentItem);
                logger.LogDebug(
                    "Adding remote file {Filename} with download URL {Url}",
                    filename,
                    primaryArtifact.DownloadUrl);

                await builder.AddRemoteFileAsync(
                    relativePath: filename,
                    downloadUrl: primaryArtifact.DownloadUrl,
                    sourceType: ContentSourceType.RemoteDownload,
                    isExecutable: false,
                    permissions: null);
            }
            else
            {
                logger.LogInformation(
                    "Content '{ContentName}' has no downloadable artifacts (dependency-only package)",
                    contentItem.Name);
            }

            AddDependencies(builder, release, contentItem, resolvedTargetGame);

            var manifest = builder.Build();

            ApplyManifestPostProcessing(
                manifest,
                contentItem,
                primaryArtifact,
                declaredPublisherId,
                resolvedName,
                discoveredItem.Id);

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
        if (primaryArtifact?.VariantAxis?.Equals("game-type", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (primaryArtifact.Variant?.Equals("Generals", StringComparison.OrdinalIgnoreCase) == true)
            {
                return GameType.Generals;
            }

            if (primaryArtifact.Variant?.Equals("Zero Hour", StringComparison.OrdinalIgnoreCase) == true ||
                primaryArtifact.Variant?.Equals("ZeroHour", StringComparison.OrdinalIgnoreCase) == true)
            {
                return GameType.ZeroHour;
            }
        }
        else if (searchResult.TargetGame != GameType.Unknown)
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
            var extension = primaryArtifact.ContentType?.ToLowerInvariant() switch
            {
                "application/zip" => ".zip",
                "application/x-rar-compressed" => ".rar",
                "application/x-7z-compressed" => ".7z",
                _ => Path.GetExtension(primaryArtifact.DownloadUrl.Split(['?', '#'])[0]) is { Length: > 1 } urlExt
                    ? urlExt
                    : ".zip",
            };
            return SanitizeFileName($"{Path.GetFileName(contentItem.Name)}{extension}");
        }

        return SanitizeFileName(Path.GetFileName(filename));
    }

    private static void AddDependencies(
        IContentManifestBuilder builder,
        ContentRelease release,
        CatalogContentItem contentItem,
        GameType resolvedTargetGame)
    {
        foreach (var dependency in release.Dependencies)
        {
            var dependencyType = CatalogManifestIdentity.ResolveDependencyContentType(dependency, contentItem);

            if (dependencyType == ContentType.GameInstallation ||
                CatalogManifestIdentity.IsBaseGameDependency(dependency))
            {
                var isGenerals = dependency.ContentId.Equals("generals", StringComparison.OrdinalIgnoreCase) ||
                                 resolvedTargetGame == GameType.Generals;
                var foundation = isGenerals &&
                                 !dependency.ContentId.Equals("zerohour", StringComparison.OrdinalIgnoreCase)
                    ? BaseDependencyBuilder.CreateGenerals108Dependency()
                    : BaseDependencyBuilder.CreateZeroHour104Dependency();

                builder.AddDependency(
                    id: foundation.Id,
                    name: foundation.Name,
                    dependencyType: ContentType.GameInstallation,
                    installBehavior: DependencyInstallBehavior.RequireExisting,
                    minVersion: dependency.VersionConstraint ?? foundation.MinVersion ?? string.Empty,
                    compatibleGameTypes: foundation.CompatibleGameTypes);
                continue;
            }

            var dependencyId = CatalogManifestIdentity.CreateContentId(
                dependency.PublisherId,
                dependencyType,
                dependency.ContentId,
                dependency.VersionConstraint);

            var installBehavior = DependencyInstallBehavior.RequireExisting;
            if (dependency.IsOptional)
            {
                installBehavior = DependencyInstallBehavior.Optional;
            }
            else if (contentItem.ContentType == ContentType.ContentBundle)
            {
                installBehavior = DependencyInstallBehavior.AutoInstall;
            }

            builder.AddDependency(
                id: ManifestId.Create(dependencyId),
                name: dependency.ContentId,
                dependencyType: dependencyType,
                installBehavior: installBehavior,
                minVersion: dependency.VersionConstraint ?? string.Empty);
        }
    }

    private static void ApplyManifestPostProcessing(
        ContentManifest manifest,
        CatalogContentItem contentItem,
        ReleaseArtifact? primaryArtifact,
        string declaredPublisherId,
        string resolvedName,
        string? searchResultId)
    {
        if (primaryArtifact != null && !string.IsNullOrWhiteSpace(primaryArtifact.Sha256))
        {
            var primaryFile = manifest.Files.FirstOrDefault();
            if (primaryFile != null)
            {
                primaryFile.Hash = primaryArtifact.Sha256;
            }
        }

        if (!string.IsNullOrWhiteSpace(searchResultId) &&
            ManifestIdValidator.IsValid(searchResultId, out _))
        {
            manifest.Id = ManifestId.Create(searchResultId);
        }

        manifest.Name = resolvedName;
        manifest.OriginalProviderName = declaredPublisherId;
        manifest.OriginalContentId = searchResultId ?? contentItem.Id;

        foreach (var dep in manifest.Dependencies)
        {
            if (dep.DependencyType == ContentType.GameInstallation && dep.CompatibleGameTypes.Count == 0)
            {
                dep.CompatibleGameTypes.Add(contentItem.TargetGame);
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

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "download.zip";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(sanitized) ? "download.zip" : sanitized;
    }
}
