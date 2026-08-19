using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using GenHub.Core.Constants;
using GenHub.Core.Utilities;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Resolves which files a manifest contributes on the current host, and which one to
/// launch.
/// <para>
/// Entry-point resolution used to be <c>Files.FirstOrDefault(f =&gt; f.IsExecutable)</c>,
/// which is order-dependent whenever more than one file qualifies, and silently picked
/// whichever the enumeration happened to yield first.
/// </para>
/// </summary>
public static class ManifestVariantResolver
{
    /// <summary>
    /// Gets the runtime identifier of the current host, for example <c>osx-arm64</c>.
    /// </summary>
    public static string CurrentRuntimeIdentifier => RuntimeInformation.RuntimeIdentifier;

    /// <summary>
    /// Selects the files a manifest contributes on the given runtime.
    /// </summary>
    /// <param name="manifest">The manifest to resolve.</param>
    /// <param name="runtimeIdentifier">Host runtime identifier; defaults to the current host.</param>
    /// <returns>
    /// The matching variant's files, or the flat <see cref="ContentManifest.Files"/> list
    /// when the manifest declares no variants. Empty when variants are declared but none
    /// matches, which means the content genuinely cannot run here.
    /// </returns>
    public static IReadOnlyList<ManifestFile> ResolveFiles(
        ContentManifest manifest,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var variant = ResolveVariant(manifest, runtimeIdentifier);

        if (variant is not null)
        {
            return variant.Files;
        }

        return manifest.Variants.Count == 0 ? manifest.Files : [];
    }

    /// <summary>
    /// Selects the variant that applies on the given runtime.
    /// </summary>
    /// <param name="manifest">The manifest to resolve.</param>
    /// <param name="runtimeIdentifier">Host runtime identifier; defaults to the current host.</param>
    /// <returns>The matching variant, or <c>null</c> when the manifest declares none or none matches.</returns>
    public static ArtifactVariant? ResolveVariant(
        ContentManifest manifest,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.Variants.Count == 0)
        {
            return null;
        }

        var rid = runtimeIdentifier ?? CurrentRuntimeIdentifier;

        // Prefer an explicit match over a platform-neutral one, so a manifest carrying
        // both a native build and a neutral asset bundle resolves to the native build.
        return manifest.Variants.FirstOrDefault(v => v.RuntimeIdentifiers.Count > 0 && v.SupportsRuntime(rid))
            ?? manifest.Variants.FirstOrDefault(v => v.RuntimeIdentifiers.Count == 0);
    }

    /// <summary>
    /// Determines whether a manifest has anything runnable or installable on the runtime.
    /// <para>
    /// Used to keep content that cannot run on this host out of the catalogue, rather
    /// than letting a user install it successfully and then find it does nothing.
    /// </para>
    /// </summary>
    /// <param name="manifest">The manifest to test.</param>
    /// <param name="runtimeIdentifier">Host runtime identifier; defaults to the current host.</param>
    /// <returns><c>true</c> when the manifest applies to the runtime.</returns>
    public static bool SupportsRuntime(ContentManifest manifest, string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.Variants.Count == 0
            || ResolveVariant(manifest, runtimeIdentifier) is not null;
    }

    /// <summary>
    /// Resolves the relative path of the file to launch.
    /// <para>
    /// The chain is deliberately explicit, and refuses to guess at the end:
    /// </para>
    /// <list type="number">
    ///   <item><description>the declared entry point, on the variant or the manifest;</description></item>
    ///   <item><description>the only file marked as needing the execute bit, if there is exactly one;</description></item>
    ///   <item><description>the only legacy launch candidate by extension, if there is exactly one;</description></item>
    ///   <item><description>otherwise fail, and report every candidate considered.</description></item>
    /// </list>
    /// </summary>
    /// <param name="manifest">The manifest to resolve.</param>
    /// <param name="runtimeIdentifier">Host runtime identifier; defaults to the current host.</param>
    /// <returns>A result carrying either the entry point or a diagnosable failure.</returns>
    public static EntryPointResolution ResolveEntryPoint(
        ContentManifest manifest,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var variant = ResolveVariant(manifest, runtimeIdentifier);
        var files = ResolveFiles(manifest, runtimeIdentifier);
        var declared = manifest.Variants.Count == 0
            ? manifest.EntryPoint
            : variant?.EntryPoint;

        if (!string.IsNullOrWhiteSpace(declared))
        {
            // A declared entry point that is not in the file list is a manifest defect.
            // Failing here is far more diagnosable than failing at Process.Start.
            var matchedFile = files.FirstOrDefault(f => PathsMatch(f.RelativePath, declared));

            return matchedFile is not null
                ? EntryPointResolution.Resolved(matchedFile.RelativePath, "declared entry point")
                : EntryPointResolution.Failed(
                    $"Manifest '{manifest.Id}' declares entry point '{declared}', which is not among its "
                    + $"{files.Count} file(s).",
                    files);
        }

        var executable = files
            .Where(f =>
                f.IsExecutable
                && (ExecutableFileClassifier.IsLegacyLaunchCandidateFromName(f.RelativePath)
                    || IsPrimaryGameExecutable(f.RelativePath)))
            .ToList();
        if (executable.Count == 1)
        {
            return EntryPointResolution.Resolved(executable[0].RelativePath, "only file requiring execute permission");
        }

        if (executable.Count == 0)
        {
            var legacy = files
                .Where(f =>
                    ExecutableFileClassifier.IsLegacyLaunchCandidateFromName(f.RelativePath)
                    || IsPrimaryGameExecutable(f.RelativePath))
                .ToList();

            if (legacy.Count == 1)
            {
                return EntryPointResolution.Resolved(legacy[0].RelativePath, "only launch candidate by extension");
            }

            var primaryFromLegacy = legacy
                .Where(f => IsPrimaryGameExecutable(f.RelativePath))
                .ToList();

            if (primaryFromLegacy.Count == 1)
            {
                return EntryPointResolution.Resolved(primaryFromLegacy[0].RelativePath, "primary game executable candidate");
            }

            return EntryPointResolution.Failed(
                legacy.Count == 0
                    ? $"Manifest '{manifest.Id}' contains no launchable file."
                    : $"Manifest '{manifest.Id}' contains {legacy.Count} possible launch targets and declares no entry point.",
                files);
        }

        var primaryExecutables = executable
            .Where(f => IsPrimaryGameExecutable(f.RelativePath))
            .ToList();

        if (primaryExecutables.Count == 1)
        {
            return EntryPointResolution.Resolved(primaryExecutables[0].RelativePath, "primary game executable candidate");
        }

        return EntryPointResolution.Failed(
            $"Manifest '{manifest.Id}' marks {executable.Count} files as requiring execute permission and "
            + "declares no entry point, so the launch target is ambiguous.",
            files);
    }

    /// <summary>
    /// Determines whether two relative file paths match, normalizing directory separators and leading slashes.
    /// </summary>
    /// <param name="left">The first relative path.</param>
    /// <param name="right">The second relative path.</param>
    /// <returns><c>true</c> if the paths match; otherwise, <c>false</c>.</returns>
    public static bool PathsMatch(string left, string right) =>
        string.Equals(
            left.Replace('\\', '/').TrimStart('/'),
            right.Replace('\\', '/').TrimStart('/'),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsPrimaryGameExecutable(string relativePath)
    {
        var fileName = System.IO.Path.GetFileName(relativePath.Replace('\\', '/'));
        return string.Equals(fileName, GameClientConstants.SuperHackersZeroHourExecutable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, GameClientConstants.SuperHackersGeneralsExecutable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, GameClientConstants.GeneralsExecutable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, GameClientConstants.SteamGameDatExecutable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, GameClientConstants.GameExecutable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, GameClientConstants.GeneralsOnline60HzExecutable, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, GameClientConstants.GeneralsOnlineDefaultExecutable, StringComparison.OrdinalIgnoreCase);
    }
}
