using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Parses GenHub-format <see cref="PublisherCatalog"/> JSON hosted by creators.
/// </summary>
/// <remarks>
/// This is the interchange format for modular catalogs: any publisher can host a schema-valid
/// file and users subscribe without a GenHub code change. Distinct from bundled
/// <see cref="ProviderDefinition"/> JSON and from proprietary catalog formats used by built-in
/// providers (e.g. GeneralsOnline API, genpatcher-dat).
/// </remarks>
public class JsonPublisherCatalogParser(ILogger<JsonPublisherCatalogParser> logger) : IPublisherCatalogParser
{
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
                () => JsonSerializer.Deserialize<PublisherCatalog>(catalogJson),
                cancellationToken);

            if (catalog == null)
            {
                return OperationResult<PublisherCatalog>.CreateFailure("Failed to deserialize catalog JSON");
            }

            NormalizeCatalogCollections(catalog);

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

        NormalizeCatalogCollections(catalog);

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
        if (string.IsNullOrWhiteSpace(catalog.Signature))
        {
            logger.LogDebug("No signature present in catalog");
            return true;
        }

        logger.LogInformation(
            "Signature present in catalog for publisher '{PublisherId}'; cryptographic verification skipped (unconfigured)",
            catalog.Publisher?.Id);
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
            if (dep == null)
            {
                errors.Add($"Content '{content.Id}' v{release.Version} has null dependency");
                continue;
            }

            if (CatalogManifestIdentity.IsBaseGameDependency(dep))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(dep.ContentId) &&
                itemsById.TryGetValue(dep.ContentId, out var sibling))
            {
                var expectedPublisherType = CatalogManifestIdentity.ResolveDeclaredPublisherType(sibling);
                if (!string.IsNullOrWhiteSpace(dep.PublisherId) &&
                    !dep.PublisherId.Equals(expectedPublisherType, StringComparison.OrdinalIgnoreCase) &&
                    !dep.PublisherId.Equals(hostPublisherId, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Dependency '{dep.ContentId}' in '{content.Id}' specifies publisherId '{dep.PublisherId}' which does not match sibling's declared publisherType '{expectedPublisherType}' or host catalog id '{hostPublisherId}'");
                }
            }
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

    private static void NormalizeCatalogCollections(PublisherCatalog catalog)
    {
        catalog.Content ??= [];
        foreach (var content in catalog.Content)
        {
            content.Tags ??= [];
            if (content.Metadata != null)
            {
                content.Metadata.ScreenshotUrls ??= [];
            }

            content.Releases ??= [];
            foreach (var release in content.Releases)
            {
                release.Artifacts ??= [];
                release.Dependencies ??= [];
            }
        }
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
        if (string.IsNullOrWhiteSpace(content.Id))
        {
            errors.Add($"Content item {index} is missing ID");
        }

        if (string.IsNullOrWhiteSpace(content.Name))
        {
            errors.Add($"Content item '{content.Id}' is missing name");
        }

        if (!string.IsNullOrWhiteSpace(content.PublisherType))
        {
            var declaredPublisher = CatalogManifestIdentity.ResolveDeclaredPublisherType(content);
            if (!content.PublisherType.Equals(declaredPublisher, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Content item '{content.Id}' has unknown publisherType '{content.PublisherType}'");
            }
        }

        if (content.Releases == null || content.Releases.Count == 0)
        {
            errors.Add($"Content item '{content.Id}' has no releases");
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

        var isDynamicRelease = release.Version.Equals("latest", StringComparison.OrdinalIgnoreCase) ||
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
                     (artifactUri.Scheme != Uri.UriSchemeHttp && artifactUri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"Artifact in '{content.Id}' v{release.Version} has invalid download URL '{artifact.DownloadUrl}'. Remote artifacts must use HTTP(S).");
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
