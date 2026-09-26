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

        var manifests = await CollectManifestsAsync(profile, cancellationToken).ConfigureAwait(false);
        var allowedBasePaths = CollectBasePaths(manifests);
        var (overlayPaths, complete) = await CollectOverlayPathsAsync(manifests, cancellationToken).ConfigureAwait(false);

        return new ProfileVerificationFileSet(
            allowedBasePaths.Count > 0 ? allowedBasePaths : null,
            overlayPaths,
            complete);
    }

    private static HashSet<string> CollectBasePaths(IReadOnlyList<ContentManifest> manifests)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in manifests)
        {
            if (manifest.ContentType != ContentType.GameInstallation && manifest.ContentType != ContentType.GameClient)
            {
                continue;
            }

            foreach (var file in manifest.Files)
            {
                if (!string.IsNullOrWhiteSpace(file.RelativePath))
                {
                    allowed.Add(file.RelativePath);
                }
            }
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

    private async Task<IReadOnlyList<ContentManifest>> CollectManifestsAsync(
        IGameProfile profile,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profile.GameClient?.Id))
        {
            ids.Add(profile.GameClient.Id);
        }

        foreach (var enabledId in profile.EnabledContentIds)
        {
            if (!string.IsNullOrWhiteSpace(enabledId))
            {
                ids.Add(enabledId);
            }
        }

        var manifests = new List<ContentManifest>(ids.Count);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifest = await TryGetManifestAsync(id, cancellationToken).ConfigureAwait(false);
            if (manifest != null)
            {
                manifests.Add(manifest);
            }
        }

        return manifests;
    }

    private async Task<ContentManifest?> TryGetManifestAsync(string manifestId, CancellationToken cancellationToken)
    {
        ManifestId id;
        try
        {
            id = ManifestId.Create(manifestId);
        }
        catch (ArgumentException ex)
        {
            logger?.LogDebug(ex, "Skipping invalid manifest ID '{ManifestId}' while resolving verification file set", manifestId);
            return null;
        }

        var result = await manifestPool.GetManifestAsync(id, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            logger?.LogDebug("Manifest '{ManifestId}' unavailable while resolving verification file set", manifestId);
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
            foreach (var file in manifest.Files)
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
