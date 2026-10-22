using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GameProfiles.Services;

/// <summary>
/// Resolves the effective verification file set for a game profile from its enabled content
/// manifests, mapping overlay archives to locally available files without downloading anything.
/// </summary>
public class ProfileVerificationFileSetService(
    IContentManifestPool manifestPool,
    ICasService casService,
    ILogger<ProfileVerificationFileSetService>? logger = null) : IProfileVerificationFileSetService
{
    private const int Sha256HexLength = 64;

    /// <inheritdoc/>
    public async Task<ProfileVerificationFileSet> GetVerificationFileSetAsync(
        IGameProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var (manifests, hasMissingManifests) = await CollectManifestsAsync(profile, cancellationToken).ConfigureAwait(false);
        var allowedBasePaths = CollectAllowedBasePaths(manifests);
        var (overlayPaths, overlaysComplete) = await CollectOverlayPathsAsync(manifests, cancellationToken).ConfigureAwait(false);

        return new ProfileVerificationFileSet(
            allowedBasePaths.Count > 0 ? allowedBasePaths : null,
            overlayPaths,
            !hasMissingManifests && overlaysComplete);
    }

    private static HashSet<string> CollectAllowedBasePaths(IReadOnlyList<ContentManifest> manifests)
    {
        // Union installation, client, and enabled overlay files: overlay content
        // materializes as loose workspace files the engine loads, so hiding those paths
        // would let modified rules verify as retail. Entries the CRC never reads
        // (non-INI, non-archive files) are harmless here.
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifests
            .SelectMany(manifest => ManifestVariantResolver.ResolveFiles(manifest))
            .Where(file => !string.IsNullOrWhiteSpace(file.RelativePath)))
        {
            allowed.Add(file.RelativePath);
        }

        return allowed;
    }

    private static bool IsBigArchive(string? relativePath)
    {
        return !string.IsNullOrWhiteSpace(relativePath) &&
            relativePath.EndsWith(SageChecksumConstants.BigFileExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha256Hash(string? hash)
    {
        return !string.IsNullOrWhiteSpace(hash) &&
            hash.Length == Sha256HexLength &&
            hash.All(char.IsAsciiHexDigit);
    }

    private async Task<(List<ContentManifest> Manifests, bool HasMissingManifests)> CollectManifestsAsync(
        IGameProfile profile,
        CancellationToken cancellationToken)
    {
        var enabledIds = new HashSet<string>(
            profile.EnabledContentIds.Where(id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.OrdinalIgnoreCase);

        var ids = new HashSet<string>(enabledIds, StringComparer.OrdinalIgnoreCase);
        var clientId = profile.GameClient?.Id;
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            ids.Add(clientId);
        }

        var manifests = new List<ContentManifest>(ids.Count);
        var hasMissingManifests = false;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ManifestId.TryCreate(id, out var manifestId))
            {
                continue;
            }

            var manifest = await TryGetManifestAsync(manifestId, cancellationToken).ConfigureAwait(false);
            if (manifest != null)
            {
                manifests.Add(manifest);
            }
            else if (enabledIds.Contains(id) && !string.Equals(id, clientId, StringComparison.OrdinalIgnoreCase))
            {
                // Fail closed when an enabled content manifest cannot be resolved. The
                // profile's own client ID is exempt: it always heads the enabled IDs,
                // and detected or catalog-chosen clients may carry IDs that are not
                // pool-backed.
                hasMissingManifests = true;
            }
        }

        return (manifests, hasMissingManifests);
    }

    private async Task<ContentManifest?> TryGetManifestAsync(ManifestId manifestId, CancellationToken cancellationToken)
    {
        var result = await manifestPool.GetManifestAsync(manifestId, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            logger?.LogDebug("Manifest '{ManifestId}' unavailable while resolving verification file set", manifestId.Value);
            return null;
        }

        return result.Data;
    }

    private async Task<(List<string> Paths, bool Complete)> CollectOverlayPathsAsync(
        IReadOnlyList<ContentManifest> manifests,
        CancellationToken cancellationToken)
    {
        var overlays = manifests
            .Where(m => m.ContentType != ContentType.GameInstallation && m.ContentType != ContentType.GameClient)
            .OrderBy(m => ContentTypePriority.GetPriority(m.ContentType))
            .ThenBy(m => m.Id.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var paths = new List<string>();
        var complete = true;
        foreach (var manifest in overlays)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolvedFiles = ManifestVariantResolver.ResolveFiles(manifest);
            if (manifest.Variants.Count > 0 && resolvedFiles.Count == 0)
            {
                // Fail closed when no declared variant applies to this host: the enabled
                // content cannot run here, so its files must not silently drop out of verification.
                complete = false;
                continue;
            }

            foreach (var file in resolvedFiles)
            {
                var resolved = await ResolveOverlayArchiveAsync(manifest, file, cancellationToken).ConfigureAwait(false);
                if (resolved != null)
                {
                    paths.Add(resolved);
                }
                else if (IsBigArchive(file.RelativePath))
                {
                    complete = false;
                }
            }
        }

        return (paths, complete);
    }

    private async Task<string?> ResolveOverlayArchiveAsync(
        ContentManifest manifest,
        ManifestFile file,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file.RelativePath))
        {
            return null;
        }

        if (!IsBigArchive(file.RelativePath))
        {
            logger?.LogDebug(
                "Skipping non-archive overlay file '{RelativePath}' of '{ManifestId}' for INI verification",
                file.RelativePath,
                manifest.Id.Value);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(file.SourcePath) && Path.IsPathRooted(file.SourcePath) && File.Exists(file.SourcePath))
        {
            return file.SourcePath;
        }

        if (!IsSha256Hash(file.Hash))
        {
            logger?.LogDebug(
                "Overlay file '{RelativePath}' of '{ManifestId}' has no usable source path or CAS hash",
                file.RelativePath,
                manifest.Id.Value);
            return null;
        }

        var casResult = await casService.GetContentPathAsync(file.Hash, cancellationToken).ConfigureAwait(false);
        if (casResult.Success && !string.IsNullOrWhiteSpace(casResult.Data) && File.Exists(casResult.Data))
        {
            return casResult.Data;
        }

        logger?.LogDebug(
            "Overlay file '{RelativePath}' of '{ManifestId}' is not available locally",
            file.RelativePath,
            manifest.Id.Value);
        return null;
    }
}
