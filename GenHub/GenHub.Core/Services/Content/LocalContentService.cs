using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Content;
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
    IContentReconciliationService reconciliationService,
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
        string? sourcePath = null,
        IProgress<ContentStorageProgress>? progress = null,
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

            var sanitizedName = SanitizeForManifestId(name);
            if (string.IsNullOrEmpty(sanitizedName))
            {
                sanitizedName = "generated-" + Guid.NewGuid().ToString("N")[..8];
                logger.LogWarning("Sanitized name for '{Name}' resulted in empty string. Using fallback: {Fallback}", name, sanitizedName);
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
            manifest.SourcePath = !string.IsNullOrEmpty(sourcePath) ? sourcePath : directoryPath;

            // Auto-add GameInstallation dependency for GameClient content types
            // This ensures auto-resolution logic works correctly for locally added clients
            if (contentType == ContentType.GameClient)
            {
                manifest.Dependencies.Add(new ContentDependency
                {
                    Id = ManifestId.Create(ManifestConstants.DefaultContentDependencyId),
                    Name = "Base Game Installation (Required)",
                    DependencyType = ContentType.GameInstallation,
                    CompatibleGameTypes = [targetGame],
                    IsOptional = false,
                });

                logger.LogInformation("Auto-added GameInstallation dependency for local GameClient");

                // Check if this looks like a GenPatcher official client (10zh, 10gn)
                // If so, we can link to the files directly if they are already in a game-like structure
                if (GenPatcherContentRegistry.IsKnownCode(name) || GenPatcherContentRegistry.IsKnownCode(sanitizedName))
                {
                    var code = GenPatcherContentRegistry.IsKnownCode(name) ? name : sanitizedName;
                    var metadata = GenPatcherContentRegistry.GetMetadata(code);

                    logger.LogInformation("Detected GenPatcher content code '{Code}' (Category: {Category})", code, metadata.Category);

                    if (metadata.Category == GenPatcherContentCategory.BaseGame)
                    {
                        logger.LogInformation("Using GameInstallation linking for legacy files in '{Code}'", code);
                        foreach (var file in manifest.Files)
                        {
                            file.SourceType = ContentSourceType.GameInstallation;
                        }
                    }
                }
            }

            // Override publisher info to mark as local content
            manifest.Publisher = new PublisherInfo
            {
                Name = LocalPublisherName,
                PublisherType = LocalPublisherType,
            };

            // Update the manifest ID to use local prefix and compliant format
            // Format: schemaVersion.userVersion.publisher.contentType.contentName
            var typeString = contentType.ToString().ToLowerInvariant();
            manifest.Id = $"1.0.{LocalPublisherType}.{typeString}.{sanitizedName}";

            // Set a dynamic version string based on current time to ensure
            // WorkspaceManager detects changes even if the name/ID remains the same.
            manifest.Version = DateTime.UtcNow.ToString("yyyyMMdd.HHmmss.fff");

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
        GameType targetGame,
        CancellationToken cancellationToken = default)
    {
        // Forward to the main method, swapping name and directoryPath to match expected signature
        return CreateLocalContentManifestAsync(directoryPath, name, contentType, targetGame, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> UpdateLocalContentManifestAsync(
        string existingManifestId,
        string name,
        string directoryPath,
        ContentType contentType,
        GameType targetGame,
        string? sourcePath = null,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Create the new manifest/content
            // We do this FIRST to ensure the new content is valid before deleting the old one
            var createResult = await CreateLocalContentManifestAsync(directoryPath, name, contentType, targetGame, sourcePath, progress, cancellationToken);

            if (!createResult.Success)
            {
                return createResult;
            }

            // 2. Orchestrate Update
            // This handles Profile ID replacement, CAS reference cleanup,
            // and removal of the old manifest from the pool.
            var reconcileResult = await reconciliationService.OrchestrateLocalUpdateAsync(
                existingManifestId,
                createResult.Data,
                cancellationToken);

            if (!reconcileResult.Success)
            {
                logger.LogWarning("Local content update orchestration failed for '{ManifestId}': {Error}", existingManifestId, reconcileResult.FirstError);

                // We still return the createResult manifest, but the old one might still be there
            }

            return createResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating local content '{ManifestId}'", existingManifestId);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to update content: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> DeleteLocalContentAsync(string manifestId, CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Deleting local content with manifest ID '{ManifestId}'", manifestId);

            // 1. Reconcile Profiles (Remove reference) and untrack CAS safely
            var reconcileResult = await reconciliationService.OrchestrateBulkRemovalAsync([manifestId], cancellationToken);
            if (!reconcileResult.Success)
            {
                logger.LogWarning("Failed to reconcile profiles for '{ManifestId}': {Error}", manifestId, reconcileResult.FirstError);
                return OperationResult.CreateFailure($"Failed to reconcile profiles: {reconcileResult.FirstError}");
            }

            // 2. Remove Content from storage
            var result = await contentStorageService.RemoveContentAsync(ManifestId.Create(manifestId), cancellationToken: cancellationToken);

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
