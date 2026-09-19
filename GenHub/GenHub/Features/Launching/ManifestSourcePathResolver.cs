using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Launching;

/// <summary>
/// Helper to resolve manifest source directories and game client paths for workspace creation.
/// </summary>
internal static class ManifestSourcePathResolver
{
    /// <summary>
    /// Resolves source paths for content manifests and game client working directories.
    /// </summary>
    /// <param name="manifests">The manifests to resolve source paths for.</param>
    /// <param name="profile">The game profile.</param>
    /// <param name="manifestPool">The content manifest pool.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary mapping manifest IDs to their resolved source paths.</returns>
    public static async Task<Dictionary<string, string>> ResolveManifestSourcePathsAsync(
        IReadOnlyList<ContentManifest> manifests,
        GameProfile profile,
        IContentManifestPool manifestPool,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var manifestSourcePaths = new Dictionary<string, string>();
        foreach (var manifest in manifests)
        {
            if (manifest.ContentType == ContentType.GameInstallation)
            {
                continue;
            }

            if (manifest.ContentType == ContentType.GameClient &&
                !string.IsNullOrEmpty(profile.GameClient?.WorkingDirectory))
            {
                manifestSourcePaths[manifest.Id.Value] = profile.GameClient.WorkingDirectory;
                logger.LogDebug("[ManifestSourcePathResolver] Source path for GameClient {ManifestId}: {SourcePath}", manifest.Id.Value, profile.GameClient.WorkingDirectory);
                continue;
            }

            var contentDirResult = await manifestPool.GetContentDirectoryAsync(manifest.Id, cancellationToken);
            if (contentDirResult.Success && !string.IsNullOrEmpty(contentDirResult.Data))
            {
                manifestSourcePaths[manifest.Id.Value] = contentDirResult.Data;
                logger.LogDebug(
                    "[ManifestSourcePathResolver] Source path for content {ManifestId} ({ContentType}): {SourcePath}",
                    manifest.Id.Value,
                    manifest.ContentType,
                    contentDirResult.Data);
            }
            else if (contentDirResult.Success)
            {
                logger.LogDebug(
                    "[ManifestSourcePathResolver] Manifest {ManifestId} ({ContentType}) is CAS-managed (no external source directory required)",
                    manifest.Id.Value,
                    manifest.ContentType);
            }
            else
            {
                logger.LogWarning(
                    "[ManifestSourcePathResolver] Could not resolve source path for manifest {ManifestId} ({ContentType}): {Error}",
                    manifest.Id.Value,
                    manifest.ContentType,
                    contentDirResult.FirstError);
            }
        }

        return manifestSourcePaths;
    }
}
