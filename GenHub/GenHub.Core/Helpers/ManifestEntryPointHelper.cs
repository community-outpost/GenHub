using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using System;

namespace GenHub.Core.Helpers;

/// <summary>
/// Shared factory-time entry-point baking. Game clients use content sniffing
/// (<see cref="GameClientEntryDetector"/>) so macOS and Linux payloads get a declared
/// entry; every other content type keeps the legacy manifest-file inference.
/// </summary>
public static class ManifestEntryPointHelper
{
    /// <summary>
    /// Bakes the entry point onto <paramref name="manifest"/> when none is declared.
    /// </summary>
    /// <param name="manifest">The manifest being built.</param>
    /// <param name="extractedDirectory">The extracted payload root.</param>
    /// <returns>
    /// Success carrying the baked entry path (<c>null</c> when nothing was baked);
    /// failure when a game client payload is ambiguous and must not ship a manifest
    /// that cannot launch.
    /// </returns>
    public static OperationResult<string?> BakeEntryPoint(ContentManifest manifest, string extractedDirectory)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            return OperationResult<string?>.CreateSuccess(manifest.EntryPoint);
        }

        if (manifest.ContentType == ContentType.GameClient)
        {
            var detection = GameClientEntryDetector.DetectEntryPoint(extractedDirectory);
            if (!detection.Success || detection.RelativePath is null)
            {
                return OperationResult<string?>.CreateFailure(
                    $"Cannot determine the launch entry for game client '{manifest.Id}': {detection}");
            }

            manifest.EntryPoint = detection.RelativePath;
            return OperationResult<string?>.CreateSuccess(detection.RelativePath);
        }

        var resolution = ManifestVariantResolver.ResolveEntryPoint(manifest);
        if (resolution.Success)
        {
            manifest.EntryPoint = resolution.RelativePath;
            return OperationResult<string?>.CreateSuccess(resolution.RelativePath);
        }

        return OperationResult<string?>.CreateSuccess(null);
    }
}
