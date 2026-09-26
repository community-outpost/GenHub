using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Parses GenHub-format <see cref="PublisherCatalog"/> JSON hosted by creators.
/// </summary>
/// <remarks>
/// This is the interchange format for modular catalogs: any publisher can host a schema-valid
/// file and users subscribe without a GenHub code change. Distinct from bundled
/// <see cref="ProviderDefinition"/> JSON and from proprietary catalog formats used by built-in
/// providers (e.g. GeneralsOnline API, genpatcher-dat). Uses <see cref="PublisherJsonOptions.CatalogImport"/>
/// to unify serialization options across import workflows, supporting trailing commas and comments.
/// </remarks>
public class JsonPublisherCatalogParser(ILogger<JsonPublisherCatalogParser> logger) : IPublisherCatalogParser
{
    private static readonly JsonSerializerOptions CatalogSerializerOptions = PublisherJsonOptions.CatalogImport;

    /// <inheritdoc />
    public async Task<OperationResult<PublisherCatalog>> ParseCatalogAsync(
        string catalogJson,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(catalogJson))
            {
                return OperationResult<PublisherCatalog>.CreateFailure("Catalog JSON is empty or null");
            }

            var catalog = await Task.Run(
                () => JsonSerializer.Deserialize<PublisherCatalog>(catalogJson, CatalogSerializerOptions),
                cancellationToken);

            if (catalog == null)
            {
                return OperationResult<PublisherCatalog>.CreateFailure("Failed to deserialize catalog JSON");
            }

            // Validate after parsing
            var validationResult = ValidateCatalog(catalog);
            if (!validationResult.Success)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(validationResult);
            }

            if (!VerifySignature(catalogJson, catalog))
            {
                return OperationResult<PublisherCatalog>.CreateFailure("Catalog signature verification failed.");
            }

            logger.LogInformation(
                "Successfully parsed catalog for publisher '{PublisherId}' with {ContentCount} content items",
                catalog.Publisher.Id,
                catalog.Content.Count);

            return OperationResult<PublisherCatalog>.CreateSuccess(catalog);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "JSON parsing error");
            return OperationResult<PublisherCatalog>.CreateFailure($"Invalid JSON format: {ex.Message}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error parsing catalog");
            return OperationResult<PublisherCatalog>.CreateFailure($"Catalog parsing failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult<bool> ValidateCatalog(PublisherCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        NormalizeCatalogCollections(catalog, logger);

        var errors = new List<string>();

        if (catalog.SchemaVersion < 1)
        {
            errors.Add($"Invalid schema version: {catalog.SchemaVersion}. Must be >= 1.");
        }

        ValidatePublisherInfo(catalog, errors);
        ValidateContentItems(catalog, errors);

        if (errors.Count > 0)
        {
            logger.LogWarning(
                "Catalog validation failed with {ErrorCount} errors: {Errors}",
                errors.Count,
                string.Join("; ", errors));
            return OperationResult<bool>.CreateFailure(errors);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <inheritdoc />
    public bool VerifySignature(string catalogJson, PublisherCatalog catalog)
    {
        if (!string.IsNullOrWhiteSpace(catalog.Signature))
        {
            logger.LogWarning(
                "Signature present in catalog for publisher '{PublisherId}', but cryptographic verification is not configured; ignoring signature",
                catalog.Publisher?.Id);
        }

        return true;
    }

    private static void ValidateDependencies(
        CatalogContentItem content,
        ContentRelease release,
        Dictionary<string, CatalogContentItem> itemsById,
        string? hostPublisherId,
        List<string> errors)
    {
        if (release.Dependencies == null)
        {
            return;
        }

        foreach (var dep in release.Dependencies)
        {
            ValidateSingleDependency(content, release, dep, itemsById, hostPublisherId, errors);
        }
    }

    private static void ValidateSingleDependency(
        CatalogContentItem content,
        ContentRelease release,
        CatalogDependency? dep,
        Dictionary<string, CatalogContentItem> itemsById,
        string? hostPublisherId,
        List<string> errors)
    {
        if (dep == null)
        {
            errors.Add($"Content '{content.Id}' v{release.Version} has null dependency");
            return;
        }

        if (!ValidateDependencyContentType(content, dep, errors))
        {
            return;
        }

        ValidateDependencyPublisher(content, dep, itemsById, hostPublisherId, errors);
    }

    private static bool ValidateDependencyContentType(
        CatalogContentItem content,
        CatalogDependency dep,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(dep.ContentType))
        {
            return !CatalogManifestIdentity.IsBaseGameDependency(dep);
        }

        if (!CatalogManifestIdentity.TryParseDeclaredContentType(dep.ContentType, out var parsedType))
        {
            errors.Add($"Dependency '{dep.ContentId}' in '{content.Id}' specifies invalid contentType '{dep.ContentType}'");
            return false;
        }

        if (CatalogManifestIdentity.IsBaseGameDependency(dep))
        {
            if (parsedType != ContentType.GameInstallation)
            {
                errors.Add($"Base game dependency '{dep.ContentId}' in '{content.Id}' cannot declare non-GameInstallation contentType '{dep.ContentType}'");
            }

            return false;
        }

        return true;
    }

    private static void ValidateDependencyPublisher(
        CatalogContentItem content,
        CatalogDependency dep,
        Dictionary<string, CatalogContentItem> itemsById,
        string? hostPublisherId,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(dep.ContentId) ||
            !itemsById.TryGetValue(dep.ContentId, out var sibling))
        {
            return;
        }

        var expectedPublisherType = CatalogManifestIdentity.ResolveDeclaredPublisherType(sibling);
        if (!string.IsNullOrWhiteSpace(dep.PublisherId) &&
            !dep.PublisherId.Equals(expectedPublisherType, StringComparison.OrdinalIgnoreCase) &&
            !dep.PublisherId.Equals(hostPublisherId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Dependency '{dep.ContentId}' in '{content.Id}' specifies publisherId '{dep.PublisherId}' which does not match sibling's declared publisherType '{expectedPublisherType}' or host catalog id '{hostPublisherId}'");
        }
    }

    private static void ValidatePublisherInfo(PublisherCatalog catalog, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(catalog.Publisher?.Id))
        {
            errors.Add("Publisher ID is required");
        }

        if (string.IsNullOrWhiteSpace(catalog.Publisher?.Name))
        {
            errors.Add("Publisher name is required");
        }
    }

    private static void NormalizeCatalogCollections(PublisherCatalog catalog, ILogger logger)
    {
        catalog.Content ??= [];
        var seenNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var content in catalog.Content.Where(content => content != null))
        {
            NormalizeContentItem(content, seenNames, logger);
        }
    }

    private static void NormalizeContentItem(CatalogContentItem content, Dictionary<string, string> seenNames, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(content.Name))
        {
            content.Name = ContentFormatPolicy.StripArchiveExtensions(content.Name);
            if (seenNames.TryGetValue(content.Name, out var existingId) && !string.Equals(existingId, content.Id, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Catalog content item '{ContentId}' normalized to display name '{ContentName}', which duplicates item '{ExistingId}'", content.Id, content.Name, existingId);
            }
            else
            {
                seenNames[content.Name] = content.Id;
            }
        }

        content.Description ??= string.Empty;
        content.Tags = content.Tags != null
            ? content.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList()
            : [];
        if (content.Metadata != null)
        {
            content.Metadata.ScreenshotUrls ??= [];
        }

        NormalizeReleases(content.Releases);
    }

    private static void NormalizeReleases(IList<ContentRelease>? releases)
    {
        if (releases == null)
        {
            return;
        }

        foreach (var release in releases.Where(release => release != null))
        {
            release.Artifacts ??= [];
            release.Dependencies ??= [];
        }
    }

    private static void ValidateBasicProperties(CatalogContentItem content, int index, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(content.Id))
        {
            errors.Add($"Content item {index} is missing ID");
        }

        if (string.IsNullOrWhiteSpace(content.Name))
        {
            errors.Add($"Content item '{content.Id}' is missing name");
        }
    }

    private static void ValidatePublisherType(CatalogContentItem content, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(content.PublisherType))
        {
            return;
        }

        var declaredPublisher = CatalogManifestIdentity.ResolveDeclaredPublisherType(content);
        if (!content.PublisherType.Equals(declaredPublisher, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Content item '{content.Id}' has unknown publisherType '{content.PublisherType}'");
        }
    }

    private static void ValidateBundleItem(CatalogContentItem content, List<string> errors)
    {
        if (content.ContentType != ContentType.ContentBundle)
        {
            return;
        }

        var hasBundledItems = content.BundledItems != null && content.BundledItems.Count > 0;
        var hasReleaseDependencies = content.Releases != null && content.Releases.Any(r => r?.Dependencies is { Count: > 0 });

        if (!hasBundledItems && !hasReleaseDependencies)
        {
            errors.Add($"Content bundle '{content.Id}' has no bundled items");
        }
    }

    private static void ValidateUpstreamItem(CatalogContentItem content, List<string> errors)
    {
        if (!CatalogConstants.UpstreamProviders.IsConfiguredUpstreamSource(content) || content.UpstreamSync == null)
        {
            return;
        }

        var provider = CatalogConstants.UpstreamProviders.Normalize(
            !string.IsNullOrWhiteSpace(content.UpstreamSync.Provider) ? content.UpstreamSync.Provider : content.PublisherType);

        if (string.Equals(provider, CatalogConstants.UpstreamProviders.GitHubReleases, StringComparison.OrdinalIgnoreCase))
        {
            var repo = content.UpstreamSync.Repository;
            if (string.IsNullOrWhiteSpace(repo) || !IsValidOwnerRepo(repo))
            {
                errors.Add($"Upstream GitHub item '{content.Id}' must declare a valid repository in 'owner/repo' format");
            }
        }
    }

    private static bool IsValidOwnerRepo(string repo)
    {
        var parts = repo.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0 && part.All(c => !char.IsWhiteSpace(c)));
    }

    private void ValidateContentItems(PublisherCatalog catalog, List<string> errors)
    {
        if (catalog.Content == null || catalog.Content.Count == 0)
        {
            errors.Add("Catalog must contain at least one content item");
            return;
        }

        var contentGroups = catalog.Content
            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id))
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in contentGroups.Where(g => g.Count() > 1))
        {
            errors.Add($"Duplicate content ID '{group.Key}'");
        }

        var itemsById = contentGroups
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < catalog.Content.Count; i++)
        {
            var content = catalog.Content[i];
            if (content == null)
            {
                errors.Add($"Content item {i} is null");
                continue;
            }

            ValidateContentItem(content, i, itemsById, catalog.Publisher?.Id, errors);
        }
    }

    private void ValidateContentItem(
        CatalogContentItem content,
        int index,
        Dictionary<string, CatalogContentItem> itemsById,
        string? hostPublisherId,
        List<string> errors)
    {
        ValidateBasicProperties(content, index, errors);
        ValidatePublisherType(content, errors);
        ValidateBundleItem(content, errors);
        ValidateUpstreamItem(content, errors);
        ValidateItemReleases(content, itemsById, hostPublisherId, errors);
    }

    private void ValidateItemReleases(
        CatalogContentItem content,
        Dictionary<string, CatalogContentItem> itemsById,
        string? hostPublisherId,
        List<string> errors)
    {
        var isUpstreamTracked = CatalogConstants.UpstreamProviders.IsConfiguredUpstreamSource(content);
        var isBundle = content.ContentType == ContentType.ContentBundle;

        if (content.Releases == null || content.Releases.Count == 0)
        {
            if (!isUpstreamTracked && !isBundle)
            {
                errors.Add($"Content item '{content.Id}' has no releases");
            }

            return;
        }

        foreach (var release in content.Releases)
        {
            ValidateRelease(content, release, itemsById, hostPublisherId, errors);
        }
    }

    private void ValidateRelease(
        CatalogContentItem content,
        ContentRelease? release,
        Dictionary<string, CatalogContentItem> itemsById,
        string? hostPublisherId,
        List<string> errors)
    {
        if (release == null)
        {
            errors.Add($"Content '{content.Id}' has null release entry");
            return;
        }

        if (string.IsNullOrWhiteSpace(release.Version))
        {
            errors.Add($"Content '{content.Id}' has release with missing version");
        }

        var hasArtifacts = release.Artifacts is { Count: > 0 };
        var hasDependencies = release.Dependencies is { Count: > 0 };

        if (hasDependencies)
        {
            ValidateDependencies(content, release, itemsById, hostPublisherId, errors);
        }

        var isDynamicRelease = release.Version?.Equals(CatalogConstants.LatestVersionToken, StringComparison.OrdinalIgnoreCase) == true ||
            content.PublisherType?.Equals(PublisherTypeConstants.TheSuperHackers, StringComparison.OrdinalIgnoreCase) == true;

        if (!hasArtifacts && !hasDependencies && !isDynamicRelease)
        {
            errors.Add($"Content '{content.Id}' release '{release.Version}' has no artifacts or dependencies");
        }
        else if (hasArtifacts)
        {
            ValidateArtifacts(content, release, errors);
        }
    }

    private void ValidateArtifacts(
        CatalogContentItem content,
        ContentRelease release,
        List<string> errors)
    {
        if (release.Artifacts == null)
        {
            return;
        }

        foreach (var artifact in release.Artifacts)
        {
            if (artifact == null)
            {
                errors.Add($"Artifact in '{content.Id}' v{release.Version} is null");
                continue;
            }

            if (string.IsNullOrWhiteSpace(artifact.DownloadUrl))
            {
                errors.Add($"Artifact in '{content.Id}' v{release.Version} missing download URL");
            }
            else if (!Uri.TryCreate(artifact.DownloadUrl, UriKind.Absolute, out var artifactUri) ||
                     artifactUri.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add($"Artifact in '{content.Id}' v{release.Version} has invalid download URL '{artifact.DownloadUrl}'. Remote artifacts must use HTTPS.");
            }

            if (string.IsNullOrWhiteSpace(artifact.Sha256))
            {
                logger.LogWarning(
                    "Artifact '{Filename}' in '{ContentId}' v{Version} has no SHA256 hash; integrity verification will be skipped",
                    artifact.Filename,
                    content.Id,
                    release.Version);
            }
        }
    }
}
