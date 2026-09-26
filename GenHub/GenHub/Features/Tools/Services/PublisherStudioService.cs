using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Service for managing Publisher Studio projects and catalogs.
/// </summary>
public class PublisherStudioService(
    ILogger<PublisherStudioService> logger,
    IPublisherCatalogParser catalogParser) : IPublisherStudioService
{
    private const string DefaultCatalogId = "default";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonSerializerOptions ProviderDefinitionJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <inheritdoc />
    public Task<OperationResult<PublisherStudioProject>> CreateProjectAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Task.FromResult(
                    OperationResult<PublisherStudioProject>.CreateFailure("Project name cannot be empty"));
            }

            var project = new PublisherStudioProject
            {
                ProjectName = name,
                ProjectPath = string.Empty,
                Catalog = new PublisherCatalog
                {
                    SchemaVersion = CatalogConstants.CatalogSchemaVersion,
                    Publisher = new PublisherProfile
                    {
                        Id = string.Empty,
                        Name = string.Empty,
                    },
                },
                LastModified = DateTime.UtcNow,
                IsDirty = true,
            };

            logger.LogInformation("Created new publisher project: {ProjectName}", name);
            return Task.FromResult(OperationResult<PublisherStudioProject>.CreateSuccess(project));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create publisher project");
            return Task.FromResult(
                OperationResult<PublisherStudioProject>.CreateFailure($"Failed to create project: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<PublisherStudioProject>> LoadProjectAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(path))
            {
                return OperationResult<PublisherStudioProject>.CreateFailure("Project file not found");
            }

            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var project = JsonSerializer.Deserialize<PublisherStudioProject>(json, JsonOptions);

            if (project == null || (project.Catalog == null && (project.Catalogs == null || project.Catalogs.Count == 0)))
            {
                return OperationResult<PublisherStudioProject>.CreateFailure("Failed to deserialize project: no valid catalog data found");
            }

            project.Catalogs ??= [];
            project.Catalog ??= new();
            project.Catalog.Publisher ??= new();
            project.Catalog.Content ??= [];
            foreach (var catalog in project.Catalogs.Select(cat => cat?.Catalog).OfType<PublisherCatalog>())
            {
                catalog.Publisher ??= project.Catalog.Publisher;
                catalog.Content ??= [];
            }

            project.ProjectPath = path;
            project.IsDirty = false;

            logger.LogInformation("Loaded publisher project from: {Path}", path);
            return OperationResult<PublisherStudioProject>.CreateSuccess(project);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load publisher project from {Path}", path);
            return OperationResult<PublisherStudioProject>.CreateFailure($"Failed to load project: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SaveProjectAsync(
        PublisherStudioProject project,
        CancellationToken cancellationToken = default)
    {
        string? tempFile = null;
        var previousLastModified = project.LastModified;
        try
        {
            if (string.IsNullOrWhiteSpace(project.ProjectPath))
            {
                return OperationResult<bool>.CreateFailure("Project path not set");
            }

            project.LastModified = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(project, JsonOptions);
            var directory = Path.GetDirectoryName(project.ProjectPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            tempFile = Path.Combine(
                directory ?? Path.GetTempPath(),
                $"{Path.GetFileName(project.ProjectPath)}.{Guid.NewGuid():N}.tmp");

            await File.WriteAllTextAsync(tempFile, json, cancellationToken);
            File.Move(tempFile, project.ProjectPath, overwrite: true);
            tempFile = null;

            project.IsDirty = false;

            logger.LogInformation("Saved publisher project to: {Path}", project.ProjectPath);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            project.LastModified = previousLastModified;
            throw;
        }
        catch (Exception ex)
        {
            project.LastModified = previousLastModified;
            logger.LogError(ex, "Failed to save publisher project to {Path}", project.ProjectPath);
            return OperationResult<bool>.CreateFailure($"Failed to save project: {ex.Message}");
        }
        finally
        {
            if (tempFile != null && File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> ExportCatalogAsync(
        PublisherStudioProject project,
        NamedCatalog? catalog = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var sourceCatalog = catalog?.Catalog ?? project.Catalog;
            var catalogName = catalog?.Name ?? DefaultCatalogId;
            var catalogToExport = JsonSerializer.Deserialize<PublisherCatalog>(
                JsonSerializer.Serialize(sourceCatalog, ExportJsonOptions),
                JsonOptions) ?? sourceCatalog;

            if (catalog != null && !string.IsNullOrWhiteSpace(catalog.IconUrl))
            {
                catalogToExport.IconUrl = catalog.IconUrl;
                catalogToExport.AvatarUrl = catalog.IconUrl;
            }

            var json = JsonSerializer.Serialize(catalogToExport, ExportJsonOptions);

            logger.LogInformation("Exported catalog '{CatalogName}' for project: {ProjectName}", catalogName, project.ProjectName);
            return Task.FromResult(OperationResult<string>.CreateSuccess(json));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export catalog");
            return Task.FromResult(OperationResult<string>.CreateFailure($"Failed to export catalog: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> ValidateCatalogAsync(
        PublisherCatalog catalog,
        bool allowPendingArtifacts = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var publisherResult = ValidatePublisherMetadata(catalog);
            if (!publisherResult.Success)
            {
                return publisherResult;
            }

            var contentResult = ValidateContentReleases(catalog, allowPendingArtifacts, cancellationToken);
            if (!contentResult.Success)
            {
                return contentResult;
            }

            // Validate content references (ExtendsContentId)
            var referenceValidation = ValidateContentReferences(catalog);
            if (!referenceValidation.Success)
            {
                return OperationResult<bool>.CreateFailure(referenceValidation);
            }

            // Use the catalog parser to validate JSON structure
            var catalogToValidate = allowPendingArtifacts
                ? PrepareCatalogForPendingArtifactValidation(catalog)
                : catalog;

            var json = JsonSerializer.Serialize(catalogToValidate);
            var parseResult = await catalogParser.ParseCatalogAsync(json, cancellationToken);

            if (!parseResult.Success)
            {
                return OperationResult<bool>.CreateFailure(parseResult);
            }

            logger.LogInformation("Catalog validation successful");
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate catalog");
            return OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string GenerateSubscriptionUrl(string catalogUrl)
    {
        return CommandLineConstants.BuildSubscriptionUrl(catalogUrl);
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> ExportProviderDefinitionAsync(
        PublisherStudioProject project,
        Dictionary<string, string> catalogHostingInfo,
        string definitionUrl,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var projectCatalog = project.Catalog;
            if (projectCatalog?.Publisher is not { } publisher)
            {
                return Task.FromResult(OperationResult<string>.CreateFailure("Publisher profile is missing"));
            }

            var catalogEntries = new List<CatalogEntry>();
            var candidateCatalogs = ResolveCandidateCatalogs(project, projectCatalog, catalogHostingInfo);

            foreach (var catalog in candidateCatalogs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (catalogHostingInfo.TryGetValue(catalog.Id, out var catalogUrl))
                {
                    var effectiveCatalogIcon = catalog.IconUrl;
                    if (string.IsNullOrWhiteSpace(effectiveCatalogIcon))
                    {
                        effectiveCatalogIcon = catalog.Catalog?.IconUrl;
                    }

                    if (string.IsNullOrWhiteSpace(effectiveCatalogIcon))
                    {
                        effectiveCatalogIcon = publisher.AvatarUrl;
                    }

                    catalogEntries.Add(new CatalogEntry
                    {
                        Id = catalog.Id,
                        Name = catalog.Name,
                        Description = catalog.Description,
                        IconUrl = effectiveCatalogIcon,
                        Url = catalogUrl,
                        Mirrors = [],
                    });
                }
            }

            if (catalogEntries.Count == 0)
            {
                return Task.FromResult(OperationResult<string>.CreateFailure("No catalogs have been published yet"));
            }

            var definition = new PublisherDefinition
            {
                SchemaVersion = CatalogConstants.DefinitionSchemaVersion,
                Publisher = new PublisherProfile
                {
                    Id = publisher.Id,
                    Name = publisher.Name,
                    Description = publisher.Description,
                    WebsiteUrl = publisher.WebsiteUrl,
                    AvatarUrl = publisher.AvatarUrl,
                    SupportUrl = publisher.SupportUrl,
                    ContactEmail = publisher.ContactEmail,
                },
                Catalogs = catalogEntries,
                DefinitionUrl = definitionUrl,
                Referrals = new List<PublisherReferral>(projectCatalog.Referrals),
                Tags = new List<string>(project.Tags),
                LastUpdated = DateTime.UtcNow,
            };

            var json = JsonSerializer.Serialize(definition, ProviderDefinitionJsonOptions);

            logger.LogInformation(
                "Exported provider definition for: {ProviderId} with {CatalogCount} catalogs",
                definition.Publisher.Id,
                catalogEntries.Count);
            return Task.FromResult(OperationResult<string>.CreateSuccess(json));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to export provider definition");
            return Task.FromResult(
                OperationResult<string>.CreateFailure($"Failed to export definition: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ValidateArtifactUrlsAsync(
        PublisherCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var errors = new System.Collections.Generic.List<string>();

            foreach (var content in catalog.Content)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateContentArtifactUrls(content, errors);
            }

            if (errors.Count > 0)
            {
                return Task.FromResult(OperationResult<bool>.CreateFailure(errors));
            }

            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to validate artifact URLs");
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Failed to validate URLs: {ex.Message}"));
        }
    }

    private static OperationResult<bool> ValidatePublisherMetadata(PublisherCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(catalog.Publisher.Id))
        {
            return OperationResult<bool>.CreateFailure("Publisher ID is required");
        }

        if (string.IsNullOrWhiteSpace(catalog.Publisher.Name))
        {
            return OperationResult<bool>.CreateFailure("Publisher name is required");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(catalog.Publisher.Id, RegexConstants.PublisherIdPattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            return OperationResult<bool>.CreateFailure("Publisher ID must be lowercase alphanumeric with hyphens only");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static OperationResult<bool> ValidateContentReleases(
        PublisherCatalog catalog,
        bool allowPendingArtifacts,
        CancellationToken cancellationToken)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var content in catalog.Content)
        {
            var result = ValidateContentItem(content, seenIds, allowPendingArtifacts, cancellationToken);
            if (!result.Success)
            {
                return result;
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static IEnumerable<NamedCatalog> ResolveCandidateCatalogs(
        PublisherStudioProject project,
        PublisherCatalog projectCatalog,
        Dictionary<string, string> catalogHostingInfo)
    {
        if (project.Catalogs.Count > 0)
        {
            return project.Catalogs;
        }

        var projectName = project.ProjectName ?? DefaultCatalogId;
        if (catalogHostingInfo.Count > 0)
        {
            return catalogHostingInfo.Keys.Select(k => new NamedCatalog
            {
                Id = k,
                Name = projectName,
                Catalog = projectCatalog,
            });
        }

        return
        [
            new NamedCatalog
            {
                Id = DefaultCatalogId,
                Name = projectName,
                Catalog = projectCatalog,
            },
        ];
    }

    private static OperationResult<bool> ValidateContentItem(
        CatalogContentItem content,
        HashSet<string> seenIds,
        bool allowPendingArtifacts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(content.Id))
        {
            return OperationResult<bool>.CreateFailure($"Content item '{content.Name}' is missing an ID");
        }

        if (!seenIds.Add(content.Id))
        {
            return OperationResult<bool>.CreateFailure($"Duplicate content item ID '{content.Id}' found in catalog");
        }

        var bundleValidation = ValidateContentBundleItem(content);
        if (!bundleValidation.Success)
        {
            return bundleValidation;
        }

        var upstreamValidation = ValidateUpstreamSyncItem(content);
        if (!upstreamValidation.Success)
        {
            return upstreamValidation;
        }

        var isUpstreamTracked = CatalogConstants.UpstreamProviders.IsConfiguredUpstreamSource(content);
        var isBundle = content.ContentType == ContentType.ContentBundle;

        if (content.Releases.Count == 0)
        {
            if (!isUpstreamTracked && !isBundle)
            {
                return OperationResult<bool>.CreateFailure($"Content item '{content.Name}' has no releases");
            }

            return OperationResult<bool>.CreateSuccess(true);
        }

        return ValidateSingleContentReleases(content, allowPendingArtifacts, isBundle, cancellationToken);
    }

    private static OperationResult<bool> ValidateContentBundleItem(CatalogContentItem content)
    {
        if (content.ContentType == ContentType.ContentBundle && (content.BundledItems == null || content.BundledItems.Count == 0))
        {
            return OperationResult<bool>.CreateFailure($"Content bundle '{content.Name}' has no bundled items");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static OperationResult<bool> ValidateUpstreamSyncItem(CatalogContentItem content)
    {
        var isUpstreamTracked = CatalogConstants.UpstreamProviders.IsConfiguredUpstreamSource(content);
        if (!isUpstreamTracked || content.UpstreamSync == null)
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        var provider = CatalogConstants.UpstreamProviders.Normalize(
            !string.IsNullOrWhiteSpace(content.UpstreamSync.Provider) ? content.UpstreamSync.Provider : content.PublisherType);

        if (string.Equals(provider, CatalogConstants.UpstreamProviders.GitHubReleases, StringComparison.OrdinalIgnoreCase))
        {
            var repo = content.UpstreamSync.Repository;
            if (string.IsNullOrWhiteSpace(repo) || !IsValidOwnerRepo(repo))
            {
                return OperationResult<bool>.CreateFailure($"Upstream GitHub item '{content.Name}' must declare a valid repository in 'owner/repo' format");
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static bool IsValidOwnerRepo(string repo)
    {
        var parts = repo.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length > 0 && part.All(c => !char.IsWhiteSpace(c)));
    }

    private static OperationResult<bool> ValidateSingleContentReleases(
        CatalogContentItem content,
        bool allowPendingArtifacts,
        bool isBundle,
        CancellationToken cancellationToken)
    {
        foreach (var release in content.Releases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(release.Version))
            {
                return OperationResult<bool>.CreateFailure($"Release in '{content.Name}' is missing a version");
            }

            if (release.Artifacts.Count == 0 && !isBundle)
            {
                return OperationResult<bool>.CreateFailure($"Release {release.Version} in '{content.Name}' has no artifacts");
            }

            if (allowPendingArtifacts)
            {
                var pendingResult = ValidatePendingArtifacts(content.Name, release);
                if (!pendingResult.Success)
                {
                    return pendingResult;
                }
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static OperationResult<bool> ValidatePendingArtifacts(string contentName, ContentRelease release)
    {
        var missingFilenameArtifact = release.Artifacts.FirstOrDefault(artifact =>
            IsPendingLocalArtifact(artifact) && string.IsNullOrWhiteSpace(artifact?.Filename));

        if (missingFilenameArtifact != null)
        {
            return OperationResult<bool>.CreateFailure($"Pending local artifact in '{contentName}' {release.Version} is missing a filename");
        }

        var missingArtifact = release.Artifacts.FirstOrDefault(artifact =>
            IsPendingLocalArtifact(artifact) &&
            !File.Exists(artifact?.LocalFilePath) &&
            !Directory.Exists(artifact?.LocalFilePath));

        if (missingArtifact != null)
        {
            return OperationResult<bool>.CreateFailure($"Local artifact file not found: '{missingArtifact.LocalFilePath}'");
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static bool IsPendingLocalArtifact(ReleaseArtifact? artifact) =>
        artifact != null && string.IsNullOrEmpty(artifact.DownloadUrl) && !string.IsNullOrEmpty(artifact.LocalFilePath);

    private static PublisherCatalog PrepareCatalogForPendingArtifactValidation(PublisherCatalog catalog)
    {
        var jsonCopy = JsonSerializer.Serialize(catalog);
        var clonedCatalog = JsonSerializer.Deserialize<PublisherCatalog>(jsonCopy)
            ?? throw new InvalidOperationException("Failed to clone publisher catalog for validation.");

        ApplyPendingArtifactUrls(catalog, clonedCatalog);
        return clonedCatalog;
    }

    private static void ApplyPendingArtifactUrls(PublisherCatalog source, PublisherCatalog target)
    {
        if (source?.Content == null || target?.Content == null)
        {
            return;
        }

        var contentCount = Math.Min(source.Content.Count, target.Content.Count);
        for (var cIdx = 0; cIdx < contentCount; cIdx++)
        {
            ApplyPendingArtifactUrlsToContent(source.Content[cIdx], target.Content[cIdx]);
        }
    }

    private static void ApplyPendingArtifactUrlsToContent(CatalogContentItem? srcContent, CatalogContentItem? tgtContent)
    {
        if (srcContent?.Releases == null || tgtContent?.Releases == null)
        {
            return;
        }

        var releaseCount = Math.Min(srcContent.Releases.Count, tgtContent.Releases.Count);
        for (var rIdx = 0; rIdx < releaseCount; rIdx++)
        {
            ApplyPendingArtifactUrlsToRelease(srcContent.Releases[rIdx], tgtContent.Releases[rIdx]);
        }
    }

    private static void ApplyPendingArtifactUrlsToRelease(ContentRelease? srcRelease, ContentRelease? tgtRelease)
    {
        if (srcRelease?.Artifacts == null || tgtRelease?.Artifacts == null)
        {
            return;
        }

        var artifactCount = Math.Min(srcRelease.Artifacts.Count, tgtRelease.Artifacts.Count);
        for (var aIdx = 0; aIdx < artifactCount; aIdx++)
        {
            var srcArtifact = srcRelease.Artifacts[aIdx];
            var tgtArtifact = tgtRelease.Artifacts[aIdx];
            if (srcArtifact != null && tgtArtifact != null && IsPendingLocalArtifact(srcArtifact))
            {
                if (string.IsNullOrWhiteSpace(tgtArtifact.Filename))
                {
                    continue;
                }

                tgtArtifact.DownloadUrl = HostingConstants.PendingUploadBaseUrl + Uri.EscapeDataString(tgtArtifact.Filename);
            }
        }
    }

    private static void ValidateContentArtifactUrls(CatalogContentItem content, List<string> errors)
    {
        foreach (var release in content.Releases)
        {
            foreach (var artifact in release.Artifacts)
            {
                ValidateSingleArtifactUrl(content.Name, release.Version, artifact, errors);
            }
        }
    }

    private static void ValidateSingleArtifactUrl(string contentName, string releaseVersion, ReleaseArtifact artifact, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(artifact.DownloadUrl))
        {
            errors.Add($"Artifact '{artifact.Filename}' in '{contentName}' {releaseVersion} has no download URL");
            return;
        }

        if (!Uri.TryCreate(artifact.DownloadUrl, UriKind.Absolute, out var uriResult)
            || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"Artifact '{artifact.Filename}' in '{contentName}' {releaseVersion} has invalid URL: {artifact.DownloadUrl}");
        }
    }

    /// <summary>
    /// Detects circular addon chains in the catalog.
    /// </summary>
    /// <param name="catalog">The catalog to check.</param>
    /// <returns>A list of error messages for any circular dependencies found.</returns>
    private static List<string> DetectCircularDependencies(PublisherCatalog catalog)
    {
        var errors = new List<string>();
        var duplicateIds = catalog.Content
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
        {
            errors.Add($"Duplicate content item IDs found: {string.Join(", ", duplicateIds)}");
        }

        var contentMap = new Dictionary<string, CatalogContentItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Content.Where(item => !string.IsNullOrWhiteSpace(item.Id)))
        {
            contentMap.TryAdd(item.Id, item);
        }

        foreach (var content in catalog.Content)
        {
            if (TryFindAddonCycle(content, contentMap, out var cycleError))
            {
                errors.Add(cycleError);
            }
        }

        return errors;
    }

    private static bool TryFindAddonCycle(
        CatalogContentItem content,
        Dictionary<string, CatalogContentItem> contentMap,
        out string cycleError)
    {
        cycleError = string.Empty;
        if (string.IsNullOrWhiteSpace(content.ExtendsContentId) || content.ExtendsContentId.Contains('/'))
        {
            return false;
        }

        var visited = new HashSet<string>();
        var currentId = content.Id;

        while (!string.IsNullOrWhiteSpace(currentId))
        {
            if (!visited.Add(currentId))
            {
                var chain = string.Join(" → ", visited) + $" → {currentId}";
                cycleError = $"Circular addon dependency detected: {chain}";
                return true;
            }

            if (!contentMap.TryGetValue(currentId, out var currentContent) ||
                string.IsNullOrWhiteSpace(currentContent.ExtendsContentId) ||
                currentContent.ExtendsContentId.Contains('/'))
            {
                break;
            }

            currentId = currentContent.ExtendsContentId;
        }

        return false;
    }

    /// <summary>
    /// Validates content references (ExtendsContentId) in the catalog.
    /// </summary>
    /// <param name="catalog">The catalog to validate.</param>
    /// <returns>An operation result indicating validation success or failure.</returns>
    private OperationResult<bool> ValidateContentReferences(PublisherCatalog catalog)
    {
        var errors = new List<string>();
        var contentIds = new HashSet<string>(catalog.Content.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

        // Regex for valid ExtendsContentId format: "contentId" or "publisherId/contentId"
        var extendsIdRegex = new System.Text.RegularExpressions.Regex(RegexConstants.ExtendsContentIdPattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

        foreach (var content in catalog.Content)
        {
            if (string.IsNullOrWhiteSpace(content.ExtendsContentId))
            {
                continue; // No reference to validate
            }

            // Validate format
            if (!extendsIdRegex.IsMatch(content.ExtendsContentId))
            {
                errors.Add($"Content '{content.Name}' has invalid ExtendsContentId format: '{content.ExtendsContentId}'. " +
                          "Must be 'contentId' or 'publisherId/contentId' with lowercase alphanumeric and hyphens only.");
                continue;
            }

            // Check if it's a same-catalog reference (no slash) and not present
            if (!content.ExtendsContentId.Contains('/') && !contentIds.Contains(content.ExtendsContentId))
            {
                errors.Add($"Content '{content.Name}' extends '{content.ExtendsContentId}' which does not exist in this catalog.");
            }

            // Cross-publisher references are validated for format only (can't verify external catalogs)
        }

        // Check for circular dependencies
        var circularErrors = DetectCircularDependencies(catalog);
        errors.AddRange(circularErrors);

        if (errors.Count > 0)
        {
            logger.LogWarning("Content reference validation failed with {ErrorCount} errors", errors.Count);
            return OperationResult<bool>.CreateFailure(errors);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }
}
