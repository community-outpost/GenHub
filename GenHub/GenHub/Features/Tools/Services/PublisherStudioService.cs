using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// Service for managing Publisher Studio projects and catalogs.
/// </summary>
public class PublisherStudioService(
    ILogger<PublisherStudioService> logger,
    IPublisherCatalogParser catalogParser) : IPublisherStudioService
{
    private readonly ILogger<PublisherStudioService> _logger = logger;
    private readonly IPublisherCatalogParser _catalogParser = catalogParser;

    /// <inheritdoc />
    public Task<OperationResult<PublisherStudioProject>> CreateProjectAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Task.FromResult(
                    OperationResult<PublisherStudioProject>.CreateFailure("Project name cannot be empty"));
            }

            // Set default project path in AppData
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var publisherStudioPath = Path.Combine(appDataPath, "GenHub", "PublisherStudio");
            Directory.CreateDirectory(publisherStudioPath);

            var projectFileName = $"{name.Replace(" ", "_")}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pubstudio";
            var projectPath = Path.Combine(publisherStudioPath, projectFileName);

            var project = new PublisherStudioProject
            {
                ProjectName = name,
                ProjectPath = projectPath,
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

            _logger.LogInformation("Created new publisher project: {ProjectName} at {Path}", name, projectPath);
            return Task.FromResult(OperationResult<PublisherStudioProject>.CreateSuccess(project));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create publisher project");
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
            var project = JsonSerializer.Deserialize<PublisherStudioProject>(json);

            if (project == null)
            {
                return OperationResult<PublisherStudioProject>.CreateFailure("Failed to deserialize project");
            }

            project.ProjectPath = path;
            project.IsDirty = false;

            _logger.LogInformation("Loaded publisher project from: {Path}", path);
            return OperationResult<PublisherStudioProject>.CreateSuccess(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load publisher project from {Path}", path);
            return OperationResult<PublisherStudioProject>.CreateFailure($"Failed to load project: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> SaveProjectAsync(
        PublisherStudioProject project,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(project.ProjectPath))
            {
                return OperationResult<bool>.CreateFailure("Project path not set");
            }

            project.LastModified = DateTime.UtcNow;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            var json = JsonSerializer.Serialize(project, options);
            await File.WriteAllTextAsync(project.ProjectPath, json, cancellationToken);

            project.IsDirty = false;

            _logger.LogInformation("Saved publisher project to: {Path}", project.ProjectPath);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save publisher project to {Path}", project.ProjectPath);
            return OperationResult<bool>.CreateFailure($"Failed to save project: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> ExportCatalogAsync(
        PublisherStudioProject project,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            var json = JsonSerializer.Serialize(project.Catalog, options);

            _logger.LogInformation("Exported catalog for project: {ProjectName}", project.ProjectName);
            return Task.FromResult(OperationResult<string>.CreateSuccess(json));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export catalog");
            return Task.FromResult(OperationResult<string>.CreateFailure($"Failed to export catalog: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> ValidateCatalogAsync(
        PublisherCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(catalog.Publisher.Id))
            {
                return OperationResult<bool>.CreateFailure("Publisher ID is required");
            }

            if (string.IsNullOrWhiteSpace(catalog.Publisher.Name))
            {
                return OperationResult<bool>.CreateFailure("Publisher name is required");
            }

            // Validate publisher ID format (lowercase, alphanumeric, hyphens)
            if (!System.Text.RegularExpressions.Regex.IsMatch(catalog.Publisher.Id, "^[a-z0-9-]+$"))
            {
                return OperationResult<bool>.CreateFailure(
                    "Publisher ID must be lowercase alphanumeric with hyphens only");
            }

            // Validate content items
            foreach (var content in catalog.Content)
            {
                if (string.IsNullOrWhiteSpace(content.Id))
                {
                    return OperationResult<bool>.CreateFailure($"Content item '{content.Name}' is missing an ID");
                }

                if (content.Releases.Count == 0)
                {
                    return OperationResult<bool>.CreateFailure($"Content item '{content.Name}' has no releases");
                }

                // Validate each release
                foreach (var release in content.Releases)
                {
                    if (string.IsNullOrWhiteSpace(release.Version))
                    {
                        return OperationResult<bool>.CreateFailure(
                            $"Release in '{content.Name}' is missing a version");
                    }

                    if (release.Artifacts.Count == 0)
                    {
                        return OperationResult<bool>.CreateFailure(
                            $"Release {release.Version} in '{content.Name}' has no artifacts");
                    }
                }
            }

            // Use the catalog parser to validate JSON structure
            var json = JsonSerializer.Serialize(catalog);
            var parseResult = await _catalogParser.ParseCatalogAsync(json, cancellationToken);

            if (!parseResult.Success)
            {
                return OperationResult<bool>.CreateFailure(parseResult);
            }

            _logger.LogInformation("Catalog validation successful");
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate catalog");
            return OperationResult<bool>.CreateFailure($"Validation failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string GenerateSubscriptionUrl(string catalogUrl)
    {
        if (string.IsNullOrWhiteSpace(catalogUrl))
        {
            return string.Empty;
        }

        return $"{CommandLineConstants.SubscribeUriPrefix}{CommandLineConstants.SubscribeUrlParam}{Uri.EscapeDataString(catalogUrl)}";
    }

    /// <inheritdoc />
    public Task<OperationResult<string>> ExportProviderDefinitionAsync(
        PublisherStudioProject project,
        string primaryCatalogUrl,
        IEnumerable<string>? catalogMirrorUrls,
        string definitionUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (project.Catalog.Publisher == null)
            {
                return Task.FromResult(OperationResult<string>.CreateFailure("Publisher profile is missing"));
            }

            var definition = new PublisherDefinition
            {
                // PublisherDefinition specific fields
                // Assuming PublisherDefinition has similar structure or mapping required
                // If PublisherDefinition is the NEW wrapper:
                Publisher = new PublisherProfile
                {
                    Id = project.Catalog.Publisher.Id,
                    Name = project.Catalog.Publisher.Name,
                    Description = project.Catalog.Publisher.Description,
                    WebsiteUrl = project.Catalog.Publisher.WebsiteUrl,
                    AvatarUrl = project.Catalog.Publisher.AvatarUrl,
                    SupportUrl = project.Catalog.Publisher.SupportUrl,
                    ContactEmail = project.Catalog.Publisher.ContactEmail,
                },
                CatalogUrl = primaryCatalogUrl,
                CatalogMirrors = catalogMirrorUrls != null ? new List<string>(catalogMirrorUrls) : [],
                DefinitionUrl = definitionUrl,
                Referrals = new List<PublisherReferral>(project.Catalog.Referrals),
                Tags = new List<string>(project.Tags),
                LastUpdated = DateTime.UtcNow,
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            };

            var json = JsonSerializer.Serialize(definition, options);

            _logger.LogInformation("Exported provider definition for: {ProviderId}", definition.Publisher.Id);
            return Task.FromResult(OperationResult<string>.CreateSuccess(json));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export provider definition");
            return Task.FromResult(
                OperationResult<string>.CreateFailure($"Failed to export definition: {ex.Message}"));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> ValidateArtifactUrlsAsync(
        PublisherCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var errors = new System.Collections.Generic.List<string>();

            foreach (var content in catalog.Content)
            {
                foreach (var release in content.Releases)
                {
                    foreach (var artifact in release.Artifacts)
                    {
                        if (string.IsNullOrWhiteSpace(artifact.DownloadUrl))
                        {
                            errors.Add($"Artifact '{artifact.Filename}' in '{content.Name}' {release.Version} has no download URL");
                            continue;
                        }

                        if (!Uri.TryCreate(artifact.DownloadUrl, UriKind.Absolute, out var uriResult)
                            || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
                        {
                            errors.Add($"Artifact '{artifact.Filename}' in '{content.Name}' {release.Version} has invalid URL: {artifact.DownloadUrl}");
                        }
                    }
                }
            }

            if (errors.Count > 0)
            {
                return Task.FromResult(OperationResult<bool>.CreateFailure(errors));
            }

            return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate artifact URLs");
            return Task.FromResult(OperationResult<bool>.CreateFailure($"URL validation failed: {ex.Message}"));
        }
    }
}
