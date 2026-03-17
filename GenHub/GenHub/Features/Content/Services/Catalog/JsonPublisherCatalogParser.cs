using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Parses GenHub-format publisher catalog JSON files.
/// </summary>
public class JsonPublisherCatalogParser(ILogger<JsonPublisherCatalogParser> logger) : IPublisherCatalogParser
{
    private readonly ILogger<JsonPublisherCatalogParser> _logger = logger;

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

            // Validate after parsing
            var validationResult = ValidateCatalog(catalog);
            if (!validationResult.Success)
            {
                return OperationResult<PublisherCatalog>.CreateFailure(validationResult);
            }

            _logger.LogInformation(
                "Successfully parsed catalog for publisher '{PublisherId}' with {ContentCount} content items",
                catalog.Publisher.Id,
                catalog.Content.Count);

            return OperationResult<PublisherCatalog>.CreateSuccess(catalog);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parsing error");
            return OperationResult<PublisherCatalog>.CreateFailure($"Invalid JSON format: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing catalog");
            return OperationResult<PublisherCatalog>.CreateFailure($"Catalog parsing failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public OperationResult<bool> ValidateCatalog(PublisherCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var errors = new List<string>();

        // Validate schema version
        if (catalog.SchemaVersion < 1)
        {
            errors.Add($"Invalid schema version: {catalog.SchemaVersion}. Must be >= 1.");
        }

        // Validate publisher info
        if (string.IsNullOrWhiteSpace(catalog.Publisher?.Id))
        {
            errors.Add("Publisher ID is required");
        }

        if (string.IsNullOrWhiteSpace(catalog.Publisher?.Name))
        {
            errors.Add("Publisher name is required");
        }

        // Validate content items
        if (catalog.Content == null || catalog.Content.Count == 0)
        {
            errors.Add("Catalog must contain at least one content item");
        }
        else
        {
            for (int i = 0; i < catalog.Content.Count; i++)
            {
                var content = catalog.Content[i];
                if (string.IsNullOrWhiteSpace(content.Id))
                {
                    errors.Add($"Content item {i} is missing ID");
                }

                if (string.IsNullOrWhiteSpace(content.Name))
                {
                    errors.Add($"Content item '{content.Id}' is missing name");
                }

                if (content.Releases == null || content.Releases.Count == 0)
                {
                    errors.Add($"Content item '{content.Id}' has no releases");
                }
                else
                {
                    // Validate each release
                    foreach (var release in content.Releases)
                    {
                        if (string.IsNullOrWhiteSpace(release.Version))
                        {
                            errors.Add($"Content '{content.Id}' has release with missing version");
                        }

                        if (release.Artifacts == null || release.Artifacts.Count == 0)
                        {
                            errors.Add($"Content '{content.Id}' release '{release.Version}' has no artifacts");
                        }
                        else
                        {
                            foreach (var artifact in release.Artifacts)
                            {
                                if (string.IsNullOrWhiteSpace(artifact.DownloadUrl))
                                {
                                    errors.Add($"Artifact in '{content.Id}' v{release.Version} missing download URL");
                                }

                                if (string.IsNullOrWhiteSpace(artifact.Sha256))
                                {
                                    errors.Add($"Artifact in '{content.Id}' v{release.Version} missing SHA256 hash");
                                }
                            }
                        }
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning("Catalog validation failed with {ErrorCount} errors", errors.Count);
            return OperationResult<bool>.CreateFailure(errors);
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <inheritdoc />
    public bool VerifySignature(string catalogJson, PublisherCatalog catalog)
    {
        // TODO: Implement catalog signature verification
        // For now, signatures are optional
        if (string.IsNullOrEmpty(catalog.Signature))
        {
            _logger.LogDebug("No signature present in catalog");
            return true;
        }

        _logger.LogWarning("Signature verification not yet implemented");
        return true;
    }
}
