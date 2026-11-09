using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.Content;

/// <summary>
/// Service for creating ContentManifests from local directories.
/// </summary>
public class LocalContentService(
    IManifestGenerationService manifestGenerationService,
    IContentStorageService contentStorageService,
    IContentReconciliationService reconciliationService,
    ILogger<LocalContentService> logger,
    IArchivePayloadProcessor? archivePayloadProcessor = null,
    ILocalizationService? localizationService = null,
    IContentManifestPool? manifestPool = null) : ILocalContentService
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
    public Task<OperationResult<ContentManifest>> CreateLocalContentManifestAsync(
        string directoryPath,
        string name,
        ContentType contentType,
        GameType targetGame,
        string? sourcePath = null,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? entryPoint = null,
        string? publisherId = null,
        string? manifestVersion = null)
    {
        return CreateLocalContentManifestAsync(
            directoryPath,
            name,
            contentType,
            targetGame,
            new LocalContentOptions
            {
                SourcePath = sourcePath,
                Progress = progress,
                CancellationToken = cancellationToken,
                EntryPoint = entryPoint,
                NormalizeInactiveArchives = true,
                PublisherId = publisherId,
                ManifestVersion = manifestVersion,
            });
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> CreateLocalContentManifestAsync(
        string directoryPath,
        string name,
        ContentType contentType,
        GameType targetGame,
        LocalContentOptions? options)
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

            var sourcePath = options?.SourcePath;
            var progress = options?.Progress;
            var cancellationToken = options?.CancellationToken ?? default;
            var entryPoint = options?.EntryPoint;
            var normalizeInactiveArchives = options?.NormalizeInactiveArchives ?? true;

            var identityName = ResolveIdentityName(name);

            logger.LogInformation(
                "Creating local content manifest for '{Name}' from '{Path}' as {ContentType}",
                name,
                directoryPath,
                contentType);

            if (archivePayloadProcessor != null)
            {
                await archivePayloadProcessor.NormalizeDirectoryStructureAsync(
                    directoryPath,
                    contentType,
                    targetGame,
                    normalizeInactiveArchives,
                    cancellationToken);
            }

            // Use the existing manifest generation service
            var builder = await manifestGenerationService.CreateContentManifestAsync(
                contentDirectory: directoryPath,
                publisherId: ResolvePublisherId(options),
                contentName: identityName,
                manifestVersion: options?.ManifestVersion,
                contentType: contentType,
                targetGame: targetGame,
                progress: progress);

            var manifest = builder.Build();
            manifest.Name = name;
            manifest.SourcePath = !string.IsNullOrEmpty(sourcePath) ? sourcePath : directoryPath;

            if (!string.IsNullOrWhiteSpace(entryPoint))
            {
                var normalizedEntryPoint = entryPoint.Replace('\\', '/').TrimStart('/');

                var segments = normalizedEntryPoint.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (Path.IsPathRooted(entryPoint) || segments.Any(s => s == ".."))
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        $"Entry point '{entryPoint}' is invalid. It must be a relative path without parent directory traversal ('..').");
                }

                var matchedFile = ManifestVariantResolver.ResolveFiles(manifest).FirstOrDefault(f =>
                    ManifestVariantResolver.PathsMatch(f.RelativePath, normalizedEntryPoint));

                if (matchedFile == null)
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        $"Entry point '{entryPoint}' was not found among the files in the directory.");
                }

                manifest.EntryPoint = matchedFile.RelativePath.Replace('\\', '/');
            }

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
                if (GenPatcherContentRegistry.IsKnownCode(name) || GenPatcherContentRegistry.IsKnownCode(identityName))
                {
                    var code = GenPatcherContentRegistry.IsKnownCode(name) ? name : identityName;
                    var metadata = GenPatcherContentRegistry.GetMetadata(code);

                    logger.LogInformation("Detected GenPatcher content code '{Code}' (Category: {Category})", code, metadata.Category);
                }
            }

            // Ingest into CAS storage
            var storageResult = await contentStorageService.StoreContentAsync(
                manifest,
                directoryPath,
                progress,
                cancellationToken);

            if (!storageResult.Success)
            {
                logger.LogError("Failed to store content in CAS: {Error}", storageResult.FirstError);
                return OperationResult<ContentManifest>.CreateFailure(
                    $"Failed to store content: {storageResult.FirstError}");
            }

            logger.LogInformation(
                "Successfully created and stored local content manifest for '{Name}' (ID: {Id})",
                name,
                manifest.Id);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating local content manifest for '{Name}'", name);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to create content manifest: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> RegisterLiveSourceAsync(
        string sourceRoot,
        string name,
        ContentType contentType,
        GameType targetGame,
        LocalContentOptions? options,
        CancellationToken cancellationToken = default)
    {
        var effectiveToken = options?.CancellationToken ?? cancellationToken;

        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (contentType is not (ContentType.Mod or ContentType.Addon or ContentType.Patch))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    GetLiveError(
                        "Profiles.AddLocalContent.Error.LiveUnsupportedType",
                        "Content type '{0}' cannot be registered as live content. Supported types: Mod, Addon, Patch.",
                        contentType));
            }

            if (!Path.IsPathFullyQualified(sourceRoot) || !Directory.Exists(sourceRoot))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    GetLiveError(
                        "Profiles.AddLocalContent.Error.LiveFolderMissing",
                        "Live content folder was not found: {0}",
                        sourceRoot));
            }

            var sourceInfo = new DirectoryInfo(sourceRoot);
            if (sourceInfo.LinkTarget != null ||
                (sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    GetLiveError(
                        "Profiles.AddLocalContent.Error.LiveReparsePoint",
                        "A live content folder cannot be a symbolic link or junction."));
            }

            var storageRoot = Path.GetFullPath(contentStorageService.GetContentStorageRoot());
            var fullSource = Path.GetFullPath(sourceRoot);
            if (PathHelper.IsPathWithinDirectory(storageRoot, fullSource) ||
                PathHelper.IsPathWithinDirectory(fullSource, storageRoot) ||
                PathHelper.AreSamePath(fullSource, storageRoot))
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    GetLiveError(
                        "Profiles.AddLocalContent.Error.LiveInsideStorage",
                        "A live content folder cannot be inside the content storage directory."));
            }

            var driveRoot = Path.GetPathRoot(fullSource);
            if (!string.IsNullOrEmpty(driveRoot) &&
                new DriveInfo(driveRoot).DriveType == DriveType.Network)
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    GetLiveError(
                        "Profiles.AddLocalContent.Error.LiveNetworkDrive",
                        "Live content must be on a local drive. Network folders are not supported."));
            }

            if (OperatingSystem.IsWindows() && !SymbolicLinkCreator.CanCreateFileLink())
            {
                return OperationResult<ContentManifest>.CreateFailure(
                    GetLiveError(
                        "Profiles.AddLocalContent.Error.LiveSymlinkUnavailable",
                        "Windows cannot create symbolic links in this session. Enable Developer Mode or run GenHub as an administrator, then register the folder again."));
            }

            var progress = options?.Progress;
            var token = effectiveToken;

            var identityName = ResolveIdentityName(name);

            logger.LogInformation(
                "Registering live content '{Name}' from '{Path}' as {ContentType}",
                name,
                fullSource,
                contentType);

            // Live mode never rewrites the source folder: no archive normalization,
            // no relocation, no hashing. The folder belongs to the developer.
            var builder = await manifestGenerationService.CreateLiveContentManifestAsync(
                contentDirectory: fullSource,
                publisherId: ResolvePublisherId(options),
                contentName: identityName,
                contentType: contentType,
                targetGame: targetGame,
                progress: progress,
                cancellationToken: token);

            var manifest = builder.Build();
            manifest.Name = name;
            manifest.SourceMode = ContentSourceMode.Live;
            manifest.SourcePath = fullSource;

            if (manifestPool != null)
            {
                var existing = await manifestPool.GetManifestAsync(manifest.Id, token);
                if (existing.Success &&
                    existing.Data is { } existingManifest &&
                    !PathHelper.AreSamePath(existingManifest.SourcePath ?? string.Empty, fullSource))
                {
                    // Two different live folders can share a display name. Never let the
                    // second registration overwrite the first folder's manifest identity.
                    var sourceAwareIdentity = ResolveSourceAwareIdentityName(name, fullSource);
                    manifest.Id = ReplaceIdentity(manifest.Id, sourceAwareIdentity);
                    logger.LogWarning(
                        "Live content '{Name}' collided with an existing manifest ID; using source-aware identity '{Identity}'",
                        name,
                        sourceAwareIdentity);
                }
            }

            if (!string.IsNullOrWhiteSpace(options?.EntryPoint))
            {
                var entryPoint = options.EntryPoint;
                var normalizedEntryPoint = entryPoint.Replace('\\', '/').TrimStart('/');
                var segments = normalizedEntryPoint.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (Path.IsPathRooted(entryPoint) || segments.Any(segment => segment == ".."))
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        GetLiveError(
                            "Profiles.AddLocalContent.Error.LiveEntryPointInvalid",
                            "Entry point '{0}' is invalid. It must be a relative path without parent directory traversal ('..').",
                            entryPoint));
                }

                var matchedFile = ManifestVariantResolver.ResolveFiles(manifest).FirstOrDefault(file =>
                    ManifestVariantResolver.PathsMatch(file.RelativePath, normalizedEntryPoint));

                if (matchedFile == null)
                {
                    return OperationResult<ContentManifest>.CreateFailure(
                        GetLiveError(
                            "Profiles.AddLocalContent.Error.LiveEntryPointMissing",
                            "Entry point '{0}' was not found among the files in the directory.",
                            entryPoint));
                }

                manifest.EntryPoint = matchedFile.RelativePath.Replace('\\', '/');
            }

            var storageResult = await contentStorageService.RegisterExternalContentAsync(
                manifest,
                fullSource,
                token);

            if (!storageResult.Success)
            {
                logger.LogError("Failed to register live content: {Error}", storageResult.FirstError);
                return OperationResult<ContentManifest>.CreateFailure(
                    GetLiveError(
                        "Profiles.AddLocalContent.Error.LiveRegistrationFailed",
                        "Failed to register live content: {0}",
                        storageResult.FirstError));
            }

            logger.LogInformation(
                "Registered live content '{Name}' (ID: {Id}) at {Path}",
                name,
                manifest.Id,
                fullSource);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (OperationCanceledException) when (effectiveToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException ex)
        {
            return CreateLiveRegistrationFailure(name, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return CreateLiveRegistrationFailure(name, ex);
        }
        catch (ArgumentException ex)
        {
            return CreateLiveRegistrationFailure(name, ex);
        }
        catch (InvalidOperationException ex)
        {
            return CreateLiveRegistrationFailure(name, ex);
        }
        catch (NotSupportedException ex)
        {
            return CreateLiveRegistrationFailure(name, ex);
        }
        catch (System.Security.SecurityException ex)
        {
            return CreateLiveRegistrationFailure(name, ex);
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
        return CreateLocalContentManifestAsync(directoryPath, name, contentType, targetGame, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<ContentManifest>> UpdateLocalContentManifestAsync(
        string existingManifestId,
        string name,
        string directoryPath,
        ContentType contentType,
        GameType targetGame,
        string? sourcePath = null,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? entryPoint = null)
    {
        return UpdateLocalContentManifestAsync(
            existingManifestId,
            name,
            directoryPath,
            contentType,
            targetGame,
            new LocalContentOptions
            {
                SourcePath = sourcePath,
                Progress = progress,
                CancellationToken = cancellationToken,
                EntryPoint = entryPoint,
                NormalizeInactiveArchives = true,
            });
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> UpdateLocalContentManifestAsync(
        string existingManifestId,
        string name,
        string directoryPath,
        ContentType contentType,
        GameType targetGame,
        LocalContentOptions? options)
    {
        try
        {
            var cancellationToken = options?.CancellationToken ?? default;

            // 1. Create the new manifest/content
            // We do this FIRST to ensure the new content is valid before deleting the old one
            var createResult = options?.SourceMode == ContentSourceMode.Live
                ? await RegisterLiveSourceAsync(directoryPath, name, contentType, targetGame, options, cancellationToken)
                : await CreateLocalContentManifestAsync(directoryPath, name, contentType, targetGame, options);

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

    private static string ResolvePublisherId(LocalContentOptions? options) =>
        string.IsNullOrWhiteSpace(options?.PublisherId) ? LocalPublisherType : options!.PublisherId!;

    /// <summary>
    /// Produces a deterministic ASCII identity segment for a user-supplied display name.
    /// Manifest IDs only accept ASCII alphanumerics, so a Chinese or punctuation-only name
    /// would otherwise normalize to an empty string and throw.
    /// </summary>
    /// <param name="name">The user-supplied display name.</param>
    /// <returns>A non-empty, deterministic identity segment.</returns>
    private static string ResolveIdentityName(string name)
    {
        var sanitized = SanitizeForManifestId(name);
        var hasNonAscii = name.Any(character => character > 127);

        if (!hasNonAscii && !string.IsNullOrEmpty(sanitized))
        {
            return sanitized;
        }

        var suffix = StableSuffix(name);
        return string.IsNullOrEmpty(sanitized) ? "content-" + suffix : sanitized + "-" + suffix;
    }

    private static string StableSuffix(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private static string ResolveSourceAwareIdentityName(string name, string sourcePath)
    {
        var sanitized = SanitizeForManifestId(name);
        var normalizedSource = Path.GetFullPath(sourcePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var suffix = StableSuffix(name + "|" + normalizedSource.ToLowerInvariant());
        return string.IsNullOrEmpty(sanitized) ? "content-" + suffix : sanitized + "-" + suffix;
    }

    private static ManifestId ReplaceIdentity(ManifestId manifestId, string identity)
    {
        var segments = manifestId.Value.Split('.');
        if (segments.Length < 2)
        {
            return manifestId;
        }

        segments[^1] = identity;
        return ManifestId.Create(string.Join('.', segments));
    }

    private OperationResult<ContentManifest> CreateLiveRegistrationFailure(string name, Exception ex)
    {
        logger.LogError(ex, "Error registering live content '{Name}'", name);
        return OperationResult<ContentManifest>.CreateFailure(
            GetLiveError(
                "Profiles.AddLocalContent.Error.LiveRegistrationFailed",
                "Failed to register live content: {0}",
                ex.Message));
    }

    private string GetLiveError(string key, string fallback, params object?[] arguments)
    {
        if (localizationService == null)
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, arguments);
        }

        var message = localizationService.GetString(key, arguments);
        return string.IsNullOrWhiteSpace(message) || string.Equals(message, key, StringComparison.Ordinal)
            ? string.Format(CultureInfo.InvariantCulture, fallback, arguments)
            : message;
    }
}
