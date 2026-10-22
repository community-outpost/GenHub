using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace GenHub.Core.Helpers;

/// <summary>
/// Shared factory-time entry-point baking. Game clients use content sniffing
/// (<see cref="GameClientEntryDetector"/>) so macOS and Linux payloads get a declared
/// entry; every other content type keeps the legacy manifest-file inference.
/// <para>
/// Declared entries are validated against the payload while it is still on disk: a
/// GameClient entry that names no file fails here instead of shipping an unlaunchable
/// manifest. Variants carry their own entries (the launch resolver ignores the root
/// entry whenever variants exist). The payload holds only the host's build, so baking
/// validates or detects the host variant's entry and leaves the other variants as declared.
/// </para>
/// </summary>
public static class ManifestEntryPointHelper
{
    /// <summary>
    /// Bakes the entry point onto <paramref name="manifest"/> when none is declared.
    /// </summary>
    /// <param name="manifest">The manifest being built.</param>
    /// <param name="extractedDirectory">The extracted payload root.</param>
    /// <param name="cancellationToken">Cancels the payload scan.</param>
    /// <param name="localizationService">Localizes failure messages shown in download UI, or <see langword="null"/> for English fallbacks.</param>
    /// <returns>
    /// Success carrying the baked entry path (<c>null</c> when nothing was baked);
    /// failure when a game client payload is ambiguous and must not ship a manifest
    /// that cannot launch.
    /// </returns>
    public static OperationResult<string?> BakeEntryPoint(
        ContentManifest manifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default,
        ILocalizationService? localizationService = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        if (manifest.ContentType == ContentType.GameClient && manifest.Variants.Count > 0)
        {
            return BakeVariantEntries(manifest, extractedDirectory, cancellationToken, localizationService);
        }

        if (!string.IsNullOrWhiteSpace(manifest.EntryPoint))
        {
            if (manifest.ContentType == ContentType.GameClient
                && !EntryExistsInPayload(extractedDirectory, manifest.EntryPoint))
            {
                return OperationResult<string?>.CreateFailure(
                    FormatFailure(localizationService, ManifestConstants.EntryPointNotFoundInPayloadKey, ManifestConstants.EntryPointNotFoundInPayload, manifest.Id, manifest.EntryPoint));
            }

            return OperationResult<string?>.CreateSuccess(manifest.EntryPoint);
        }

        if (manifest.ContentType == ContentType.GameClient)
        {
            var detection = GameClientEntryDetector.DetectEntryPoint(extractedDirectory, cancellationToken);
            if (!detection.Success)
            {
                return OperationResult<string?>.CreateFailure(
                    FormatFailure(localizationService, ManifestConstants.EntryPointDetectionFailedKey, ManifestConstants.EntryPointDetectionFailed, manifest.Id, detection));
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

    private static OperationResult<string?> BakeVariantEntries(
        ContentManifest manifest,
        string extractedDirectory,
        CancellationToken cancellationToken,
        ILocalizationService? localizationService)
    {
        // The payload holds only this platform's build, so only the host variant can be
        // validated or detected against it. Other variants keep their declared entries.
        var variant = ManifestVariantResolver.ResolveVariant(manifest);
        if (variant is null)
        {
            return OperationResult<string?>.CreateSuccess(null);
        }

        if (!string.IsNullOrWhiteSpace(variant.EntryPoint))
        {
            return EntryExistsInPayload(extractedDirectory, variant.EntryPoint)
                ? OperationResult<string?>.CreateSuccess(variant.EntryPoint)
                : OperationResult<string?>.CreateFailure(
                    FormatFailure(localizationService, ManifestConstants.VariantEntryPointNotFoundInPayloadKey, ManifestConstants.VariantEntryPointNotFoundInPayload, manifest.Id, variant.EntryPoint));
        }

        var detection = GameClientEntryDetector.DetectEntryPoint(extractedDirectory, cancellationToken);
        if (!detection.Success)
        {
            return OperationResult<string?>.CreateFailure(
                FormatFailure(localizationService, ManifestConstants.EntryPointDetectionFailedKey, ManifestConstants.EntryPointDetectionFailed, manifest.Id, detection));
        }

        variant.EntryPoint = detection.RelativePath;
        return OperationResult<string?>.CreateSuccess(detection.RelativePath);
    }

    private static string FormatFailure(ILocalizationService? localizationService, string key, string fallbackFormat, params object?[] args)
    {
        if (localizationService is not null)
        {
            return localizationService.GetString(key, args);
        }

        return string.Format(CultureInfo.InvariantCulture, fallbackFormat, args);
    }

    private static bool EntryExistsInPayload(string extractedDirectory, string entryPoint)
    {
        var resolved = ContentPathPolicy.ResolveContainedFile(extractedDirectory, entryPoint);
        if (!resolved.Success || string.IsNullOrWhiteSpace(resolved.Data))
        {
            return false;
        }

        return File.Exists(resolved.Data) || Directory.Exists(resolved.Data);
    }
}
