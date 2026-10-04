using GenHub.Core.Constants;
using GenHub.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

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
    /// matches, which means the content genuinely cannot run here. Null entries are
    /// skipped; structural validation reports them through <see cref="GetDeclaredFileLists"/>.
    /// </returns>
    public static IReadOnlyList<ManifestFile> ResolveFiles(
        ContentManifest manifest,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var variant = ResolveVariant(manifest, runtimeIdentifier);
        IReadOnlyList<ManifestFile>? files = null;
        if (variant is not null)
        {
            files = variant.Files;
        }
        else if (manifest.Variants.Count == 0)
        {
            files = manifest.Files;
        }

        return files is null ? [] : files.Where(f => f is not null).ToList();
    }

    /// <summary>
    /// Enumerates every file the manifest declares: the flat list followed by each
    /// variant's files, whichever runtime they target. Null entries are skipped; use
    /// GetDeclaredFileLists for structural validation of malformed entries.
    /// </summary>
    /// <param name="manifest">The manifest to enumerate.</param>
    /// <returns>All declared files.</returns>
    public static IReadOnlyList<ManifestFile> EnumerateAllFiles(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var variantFiles = manifest.Variants.SelectMany(v => v?.Files ?? []);
        return (manifest.Files ?? []).Concat(variantFiles).Where(f => f is not null).ToList();
    }

    /// <summary>Gets the declared file lists without dropping malformed entries, for structural validation.</summary>
    /// <param name="manifest">The manifest to inspect.</param>
    /// <returns>The flat list followed by each variant's list, in declaration order.</returns>
    public static IReadOnlyList<IReadOnlyList<ManifestFile>?> GetDeclaredFileLists(ContentManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var lists = new List<IReadOnlyList<ManifestFile>?> { manifest.Files };
        lists.AddRange(manifest.Variants.Select(v => (IReadOnlyList<ManifestFile>?)v?.Files));
        return lists;
    }

    /// <summary>
    /// Replaces the files <see cref="ResolveFiles"/> returns for the given runtime.
    /// <para>
    /// The matching variant's list is replaced, or the flat list when the manifest
    /// declares no variants. Other variants are left untouched. Nothing changes when
    /// variants are declared but none matches.
    /// </para>
    /// </summary>
    /// <param name="manifest">The manifest to update.</param>
    /// <param name="files">The new files.</param>
    /// <param name="runtimeIdentifier">Host runtime identifier; defaults to the current host.</param>
    public static void ReplaceResolvedFiles(
        ContentManifest manifest,
        List<ManifestFile> files,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);

        var variant = ResolveVariant(manifest, runtimeIdentifier);
        if (variant is not null)
        {
            variant.Files = files;
        }
        else if (manifest.Variants.Count == 0)
        {
            manifest.Files = files;
        }
    }

    /// <summary>
    /// Copies a manifest and makes the given files the ones it resolves to on the runtime: the
    /// matching variant's files, or the flat list when the manifest declares no variants.
    /// <para>
    /// For a manifest built from a delivered payload. The other variants are kept, so the copy
    /// still describes every platform the release supports.
    /// </para>
    /// </summary>
    /// <param name="original">The manifest to copy.</param>
    /// <param name="files">The files delivered for the runtime.</param>
    /// <param name="runtimeIdentifier">Host runtime identifier; defaults to the current host.</param>
    /// <returns>The copy.</returns>
    /// <exception cref="InvalidOperationException">
    /// The manifest declares variants and none matches the runtime, so there is nowhere to put the files.
    /// </exception>
    public static ContentManifest CopyWithResolvedFiles(
        ContentManifest original,
        List<ManifestFile> files,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(files);

        if (!SupportsRuntime(original, runtimeIdentifier))
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                ManifestErrorMessages.NoHostVariantForManifest,
                original.Id,
                runtimeIdentifier ?? CurrentRuntimeIdentifier));
        }

        var copy = new ContentManifest(original);
        ReplaceResolvedFiles(copy, files, runtimeIdentifier);
        return copy;
    }

    /// <summary>
    /// Rewrites every file the manifest declares, in the flat list and in each variant.
    /// </summary>
    /// <param name="manifest">The manifest to update.</param>
    /// <param name="rewrite">Returns the replacement for a file, or the file itself to keep it.</param>
    public static void RewriteAllFiles(ContentManifest manifest, Func<ManifestFile, ManifestFile> rewrite)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(rewrite);

        manifest.Files = (manifest.Files ?? []).Select(f => f is null ? f! : rewrite(f)).ToList();
        foreach (var variant in manifest.Variants.Where(v => v is not null))
        {
            variant.Files = (variant.Files ?? []).Select(f => f is null ? f! : rewrite(f)).ToList();
        }
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
        return manifest.Variants.FirstOrDefault(v => v is not null && v.RuntimeIdentifiers is { Count: > 0 } && v.SupportsRuntime(rid))
            ?? manifest.Variants.FirstOrDefault(v => v is not null && v.RuntimeIdentifiers is null or { Count: 0 });
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
    /// Gets the declared entry point without falling back to detection: the matching
    /// variant's when variants are declared, otherwise the manifest-level one.
    /// </summary>
    /// <param name="manifest">The manifest to inspect.</param>
    /// <param name="runtimeIdentifier">Host runtime identifier; defaults to the current host.</param>
    /// <returns>The declared relative entry point, or <c>null</c> when none applies.</returns>
    public static string? GetDeclaredEntryPoint(ContentManifest manifest, string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.Variants.Count == 0
            ? manifest.EntryPoint
            : ResolveVariant(manifest, runtimeIdentifier)?.EntryPoint;
    }

    /// <summary>
    /// Resolves the relative path of the file to launch.
    /// <para>
    /// The chain is deliberately explicit, and refuses to guess at the end:
    /// </para>
    /// <list type="number">
    ///   <item><description>the declared entry point, on the variant or the manifest;</description></item>
    ///   <item><description>the only file, when the manifest carries a lone Flatpak bundle;</description></item>
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

        var files = ResolveFiles(manifest, runtimeIdentifier);
        var declared = GetDeclaredEntryPoint(manifest, runtimeIdentifier);

        if (!string.IsNullOrWhiteSpace(declared))
        {
            return ResolveDeclaredEntryPoint(manifest, files, declared);
        }

        // Single-file Flatpak downloads never pass through payload extraction, so no
        // entry point is baked for them. The lone bundle is unambiguous: resolving it
        // lets the launch pipeline provision it instead of failing here and falling
        // back to an unrelated executable.
        if (IsLoneFlatpakBundle(files))
        {
            return EntryPointResolution.Resolved(files[0].RelativePath, "only Flatpak bundle");
        }

        var executable = files
            .Where(f =>
                f.IsExecutable
                && (ExecutableFileClassifier.IsLegacyLaunchCandidateFromName(f.RelativePath)
                    || IsPrimaryGameExecutable(f.RelativePath)
                    || f.RelativePath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (executable.Count == 1)
        {
            return EntryPointResolution.Resolved(executable[0].RelativePath, "only file requiring execute permission");
        }

        return executable.Count == 0
            ? ResolveLegacyCandidates(manifest, files)
            : ResolvePrimaryExecutable(manifest, files, executable);
    }

    /// <summary>
    /// Resolves the declared launch relationship: the process the resolved entry point
    /// spawns and hands the session to.
    /// <para>
    /// Mirrors entry-point placement: the matching variant's relationship wins when
    /// variants are declared, otherwise the manifest-level one. Absence is normal and
    /// means the entry is the game itself. An invalid declaration is treated as absent
    /// so a bad relationship degrades to legacy guessing instead of breaking the launch.
    /// </para>
    /// </summary>
    /// <param name="manifest">The manifest to resolve.</param>
    /// <param name="runtimeIdentifier">Host runtime identifier; defaults to the current host.</param>
    /// <returns>The validated relationship, or <c>null</c> when none is declared or valid.</returns>
    public static LaunchRelationship? ResolveLaunchRelationship(
        ContentManifest manifest,
        string? runtimeIdentifier = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var variant = ResolveVariant(manifest, runtimeIdentifier);
        var declared = manifest.Variants.Count == 0
            ? manifest.LaunchRelationship
            : variant?.LaunchRelationship;

        if (declared is null || !LaunchRelationship.IsValidProcessName(declared.ProcessName))
        {
            return null;
        }

        return declared;
    }

    /// <summary>
    /// Determines whether a file list is exactly one Flatpak bundle, the shape a
    /// single-asset Flatpak download takes before content storage persists it.
    /// </summary>
    /// <param name="files">The files to inspect.</param>
    /// <returns><c>true</c> when the list holds a single <c>.flatpak</c> file.</returns>
    public static bool IsLoneFlatpakBundle(IReadOnlyList<ManifestFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        return files.Count == 1
            && files[0].RelativePath.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase);
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

    private static EntryPointResolution ResolveDeclaredEntryPoint(
        ContentManifest manifest,
        IReadOnlyList<ManifestFile> files,
        string declared)
    {
        var matchedFile = files.FirstOrDefault(f => PathsMatch(f.RelativePath, declared));

        return matchedFile is not null
            ? EntryPointResolution.Resolved(matchedFile.RelativePath, "declared entry point")
            : EntryPointResolution.Failed(
                $"Manifest '{manifest.Id}' declares entry point '{declared}', which is not among its "
                + $"{files.Count} file(s).",
                files);
    }

    private static EntryPointResolution ResolveLegacyCandidates(
        ContentManifest manifest,
        IReadOnlyList<ManifestFile> files)
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

        if (TryResolveSinglePrimaryExecutable(legacy, out var primaryResolution))
        {
            return primaryResolution;
        }

        return EntryPointResolution.Failed(
            legacy.Count == 0
                ? $"Manifest '{manifest.Id}' contains no launchable file."
                : $"Manifest '{manifest.Id}' contains {legacy.Count} possible launch targets and declares no entry point.",
            files);
    }

    private static EntryPointResolution ResolvePrimaryExecutable(
        ContentManifest manifest,
        IReadOnlyList<ManifestFile> files,
        IReadOnlyList<ManifestFile> executable)
    {
        if (TryResolveSinglePrimaryExecutable(executable, out var primaryResolution))
        {
            return primaryResolution;
        }

        return EntryPointResolution.Failed(
            $"Manifest '{manifest.Id}' marks {executable.Count} files as requiring execute permission and "
            + "declares no entry point, so the launch target is ambiguous.",
            files);
    }

    private static bool TryResolveSinglePrimaryExecutable(
        IReadOnlyList<ManifestFile> candidates,
        out EntryPointResolution resolution)
    {
        var primary = candidates
            .Where(f => IsPrimaryGameExecutable(f.RelativePath))
            .ToList();

        if (primary.Count == 1)
        {
            resolution = EntryPointResolution.Resolved(primary[0].RelativePath, "primary game executable candidate");
            return true;
        }

        resolution = default!;
        return false;
    }

    private static bool IsPrimaryGameExecutable(string relativePath)
    {
        var fileName = System.IO.Path.GetFileName(relativePath.Replace('\\', '/'));
        return GameClientConstants.ValidGameExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);
    }
}
