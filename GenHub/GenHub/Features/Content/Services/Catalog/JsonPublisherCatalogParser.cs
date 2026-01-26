using System;
using System.Collections.Generic;
using System.IO;
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

    /// <summary>
    /// Parses a GenHub-format publisher catalog from the provided JSON and validates its contents.
    /// </summary>
    /// <param name="catalogJson">A JSON string containing a GenHub-format publisher catalog.</param>
    /// <param name="cancellationToken">A token to cancel the parsing operation.</param>
    /// <returns>
    /// An <see cref="OperationResult{PublisherCatalog}"/> containing the parsed <see cref="PublisherCatalog"/> on success;
    /// on failure, contains error information describing why deserialization or validation failed.
    /// </returns>
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

            // Use MemoryStream to support Async deserialization for better responsiveness
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(catalogJson));
            var catalog = await JsonSerializer.DeserializeAsync<PublisherCatalog>(stream, cancellationToken: cancellationToken);

            if (catalog == null)
            {
                return OperationResult<PublisherCatalog>.CreateFailure("Failed to deserialize catalog JSON");
            }

            // Verify signature
            var signatureVerificationResult = VerifySignature(catalogJson, catalog);
            if (!signatureVerificationResult.Success)
            {
                var errorMessage = $"Catalog signature verification failed: {signatureVerificationResult.FirstError}";
                _logger.LogError("Catalog signature verification failed: {ErrorMessage}", signatureVerificationResult.FirstError);
                return OperationResult<PublisherCatalog>.CreateFailure(errorMessage);
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
        catch (OperationCanceledException)
        {
            throw;
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

    /// <summary>
    /// Validates a PublisherCatalog for required schema version, publisher metadata, content items, releases, and artifact fields.
    /// </summary>
    /// <param name="catalog">The catalog to validate. Must not be null.</param>
    /// <returns>
    /// An <see cref="OperationResult{T}"/> containing `true` when the catalog passes all validations; otherwise a failure result containing a list of validation error messages.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="catalog"/> is null.</exception>
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

    /// <summary>
    /// Determines whether the publisher catalog's signature is valid.
    /// If the catalog contains no signature, returns true. If a signature is present, signature verification is not yet implemented and the method currently returns a failure result.
    /// </summary>
    /// <param name="catalogJson">The raw catalog JSON used as the source for signature verification.</param>
    /// <param name="catalog">The parsed <see cref="PublisherCatalog"/> whose <c>Signature</c> is checked.</param>
    /// <returns>An OperationResult containing `true` if no signature is present; otherwise a failure result (rejecting signed catalogs until verification is implemented).</returns>
    public OperationResult<bool> VerifySignature(string catalogJson, PublisherCatalog catalog)
    {
        // TODO: Implement catalog signature verification (Tracking issue: GH-123)
        // For now, signatures are optional
        if (string.IsNullOrEmpty(catalog.Signature))
        {
            _logger.LogDebug("No signature present in catalog");
            return OperationResult<bool>.CreateSuccess(true);
        }

        // Fail-secure: reject signed catalogs until verification is implemented
        _logger.LogError("Signature verification not yet implemented - rejecting signed catalog");
        return OperationResult<bool>.CreateFailure("Catalog signature verification not yet implemented");
    }
}