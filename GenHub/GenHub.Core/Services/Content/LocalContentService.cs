using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Core.Services.Content;

/// <summary>
/// Service for creating ContentManifests from local directories.
/// </summary>
public class LocalContentService(
    IManifestGenerationService manifestGenerationService,
    IContentStorageService contentStorageService,
    ILogger<LocalContentService> logger) : ILocalContentService
{
    /// <summary>
    /// The publisher name for locally-generated content.
    /// </summary>
    public const string LocalPublisherName = "GenHub (Local)";

    /// <summary>
    /// The publisher type for locally-generated content.
    /// </summary>
    public const string LocalPublisherType = "local";

    /// <inheritdoc />
    public IReadOnlyList<ContentType> AllowedContentTypes { get; } =
    [
        ContentType.GameClient,
        ContentType.Addon,
        ContentType.Map,
        ContentType.MapPack,
        ContentType.Mission,
        ContentType.Mod,
        ContentType.ModdingTool,
        ContentType.Executable,
        ContentType.Patch,
    ];

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> CreateLocalContentManifestAsync(
        string directoryPath,
        string name,
        ContentType contentType,
        GameType targetGame,
        IProgress<GenHub.Core.Models.Content.ContentStorageProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (!AllowedContentTypes.Contains(contentType))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Content type '{contentType}' is not allowed for local content. " +
                    $"Allowed types: {string.Join(", ", AllowedContentTypes)}");
            }

            if (!Directory.Exists(directoryPath))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Directory not found: {directoryPath}");
            }

            logger.LogInformation(
                "Creating local content manifest for '{Name}' from '{Path}' as {ContentType}",
                name,
                directoryPath,
                contentType);

            // Use the existing manifest generation service
            var builder = await manifestGenerationService.CreateContentManifestAsync(
                contentDirectory: directoryPath,
                publisherId: LocalPublisherType,
                contentName: name,
                manifestVersion: 0,
                contentType: contentType,
                targetGame: targetGame);

            var manifest = builder.Build();

            // Auto-add GameInstallation dependency for GameClient content types
            // This ensures auto-resolution logic works correctly for locally added clients
            if (contentType == ContentType.GameClient)
            {
                manifest.Dependencies.Add(new ContentDependency
                {
                    Id = ManifestId.Create(ManifestConstants.DefaultContentDependencyId),
                    DependencyType = ContentType.GameInstallation,
                    CompatibleGameTypes = [targetGame],
                    IsOptional = false,
                });

                logger.LogInformation("Auto-added GameInstallation dependency for local GameClient");
            }

            // Override publisher info to mark as local content
            manifest.Publisher = new PublisherInfo
            {
                Name = LocalPublisherName,
                PublisherType = LocalPublisherType,
            };

            // Update the manifest ID to use local prefix and compliant format
            // Format: schemaVersion.userVersion.publisher.contentType.contentName
            var sanitizedName = SanitizeForManifestId(name);
            var typeString = contentType.ToString().ToLowerInvariant();
            manifest.Id = $"1.0.{LocalPublisherType}.{typeString}.{sanitizedName}";

            logger.LogInformation(
                "Created local content manifest with ID '{Id}' for '{Name}'",
                manifest.Id,
                name);

            // Store content in CAS
            var storageResult = await contentStorageService.StoreContentAsync(manifest, directoryPath, progress, cancellationToken);
            if (!storageResult.Success)
            {
                return OperationResult<ContentManifest>.CreateFailure($"Failed to store local content: {storageResult.FirstError}");
            }

            return OperationResult<ContentManifest>.CreateSuccess(storageResult.Data);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create local content manifest for '{Name}'", name);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to create manifest: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<ContentManifest>> AddLocalContentAsync(
        string name,
        string directoryPath,
        ContentType contentType,
        GameType targetGame)
    {
        // Forward to the main method, swapping name and directoryPath to match expected signature
        return CreateLocalContentManifestAsync(directoryPath, name, contentType, targetGame);
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteLocalContentAsync(string manifestId)
    {
        try
        {
            logger.LogInformation("Deleting local content with manifest ID '{ManifestId}'", manifestId);

            // Deleting local content involves:
            // 1. Removing the manifest from the storage/pool
            // 2. Potentially removing the local files if they were managed/copied by GenHub (via CAS)

            // For now, we primarily just remove the content from the storage service
            var result = await contentStorageService.RemoveContentAsync(ManifestId.Create(manifestId));

            if (!result.Success)
            {
                logger.LogWarning("Failed to delete local content '{ManifestId}': {Error}", manifestId, result.FirstError);
                return OperationResult.CreateFailure(result.FirstError ?? "Unknown error occurred during deletion");
            }

            logger.LogInformation("Successfully deleted local content '{ManifestId}'", manifestId);
            return OperationResult.CreateSuccess();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting local content '{ManifestId}'", manifestId);
            return OperationResult.CreateFailure($"Failed to delete content: {ex.Message}");
        }
    }

    /// <summary>
    /// Sanitizes a name for use in a manifest ID.
    /// </summary>
    private static string SanitizeForManifestId(string name)
    {
        // Replace spaces and special chars with hyphens, lowercase
        var sanitized = name.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');

        // Remove any characters that aren't alphanumeric or hyphens
        sanitized = string.Concat(sanitized.Where(c => char.IsLetterOrDigit(c) || c == '-'));

        // Collapse multiple hyphens and trim
        while (sanitized.Contains("--"))
        {
            sanitized = sanitized.Replace("--", "-");
        }

        return sanitized.Trim('-');
    }
}
