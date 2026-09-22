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
/// entry whenever variants exist), so baking fills missing variant entries and skips
/// sniffing entirely when every variant already declares one.
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
        var dangling = manifest.Variants
            .Where(v => !string.IsNullOrWhiteSpace(v.EntryPoint))
            .FirstOrDefault(v => !EntryExistsInPayload(extractedDirectory, v.EntryPoint!));
        if (dangling is not null)
        {
            return OperationResult<string?>.CreateFailure(
                FormatFailure(localizationService, ManifestConstants.VariantEntryPointNotFoundInPayloadKey, ManifestConstants.VariantEntryPointNotFoundInPayload, manifest.Id, dangling.EntryPoint));
        }

        var missing = manifest.Variants.Where(v => string.IsNullOrWhiteSpace(v.EntryPoint)).ToList();
        if (missing.Count == 0)
        {
            return OperationResult<string?>.CreateSuccess(null);
        }

        var detection = GameClientEntryDetector.DetectEntryPoint(extractedDirectory, cancellationToken);
        if (!detection.Success)
        {
            return OperationResult<string?>.CreateFailure(
                FormatFailure(localizationService, ManifestConstants.EntryPointDetectionFailedKey, ManifestConstants.EntryPointDetectionFailed, manifest.Id, detection));
        }

        // Detection scans the whole payload, so it cannot produce one entry per
        // variant: fail loudly instead of assigning one variant's entry to another.
        if (missing.Count > 1)
        {
            return OperationResult<string?>.CreateFailure(
                FormatFailure(localizationService, ManifestConstants.MultipleVariantsMissingEntryPointKey, ManifestConstants.MultipleVariantsMissingEntryPoint, manifest.Id, missing.Count));
        }

        missing[0].EntryPoint = detection.RelativePath;
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
