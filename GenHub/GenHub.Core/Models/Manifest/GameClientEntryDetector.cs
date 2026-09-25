using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Detects the launch entry point of an extracted game client payload at factory time so
/// the answer is baked as the manifest's declared entry point.
/// <para>
/// An explicitly declared entry always wins; this detector only runs when none is declared
/// (launch-time resolution checks the declaration first, so that contract is untouched).
/// macOS <c>.app</c> bundles are found at any depth because wrapper-stripping may remove
/// the <c>.app</c> level itself, leaving a bare <c>Contents</c> tree behind. Root-level
/// scripts are never auto-picked: only a plist-declared script becomes the entry, which
/// preserves the classifier's scripts-are-not-launch-candidates contract.
/// </para>
/// <para>
/// Plist-declared names are untrusted input: absolute paths, separators, and parent
/// traversal are rejected, and every resolved path is verified to stay under the payload
/// root, so a hostile archive cannot bake a workspace-escaping entry point.
/// </para>
/// </summary>
public static class GameClientEntryDetector
{
    private const string AppExtension = ".app";
    private const string FrameworkExtension = ".framework";
    private const string ContentsDirectoryName = "Contents";
    private const string FrameworksDirectoryName = "Frameworks";
    private const string PlugInsDirectoryName = "PlugIns";
    private const string MacOsDirectoryName = "MacOS";
    private const string InfoPlistFileName = "Info.plist";
    private const string BundleExecutableKey = "CFBundleExecutable";
    private const string ExeExtension = ".exe";

    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
    };

    private static readonly char[] DirectorySeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>
    /// Detects the entry point of an extracted game client payload.
    /// </summary>
    /// <param name="extractedDirectory">The extracted payload root.</param>
    /// <param name="cancellationToken">Cancels the payload scan.</param>
    /// <returns>
    /// A resolution carrying the entry's path relative to <paramref name="extractedDirectory"/>,
    /// or a failure listing every candidate considered.</returns>
    public static EntryPointResolution DetectEntryPoint(string extractedDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(extractedDirectory) || !Directory.Exists(extractedDirectory))
        {
            return EntryPointResolution.Failed(
                $"Payload directory '{extractedDirectory}' does not exist.",
                Array.Empty<string>());
        }

        var bundleRoots = FindBundleRoots(extractedDirectory, cancellationToken);
        if (bundleRoots.Count > 1)
        {
            return EntryPointResolution.Failed(
                $"Payload contains {bundleRoots.Count} application bundles and declares no entry point.",
                bundleRoots.Select(b => ToRelativePath(extractedDirectory, b)));
        }

        if (bundleRoots.Count == 1)
        {
            return ResolveBundleEntryPoint(extractedDirectory, bundleRoots[0], cancellationToken);
        }

        return ResolveFlatEntryPoint(extractedDirectory, cancellationToken);
    }

    /// <summary>
    /// Inspects an archive (e.g. .zip) directly to detect its game client entry point without requiring extraction.
    /// </summary>
    /// <param name="archivePath">Path to the archive file.</param>
    /// <returns>Detected entry point relative path, or null if undetermined.</returns>
    public static string? DetectEntryPointFromArchive(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            return null;
        }

        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(archivePath);
            var fileEntries = zip.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name) && IsSafeArchiveRelativePath(e.FullName))
                .ToList();

            // 1. Look for known game binaries
            var known = fileEntries
                .Where(e => GameClientConstants.ValidGameExecutableNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (known.Count == 1)
            {
                return known[0].FullName.Replace('\\', '/');
            }

            if (known.Count > 1)
            {
                var minDepth = known.Min(e => e.FullName.Split('/', '\\').Length);
                var shallow = known.Where(e => e.FullName.Split('/', '\\').Length == minDepth).ToList();
                if (shallow.Count == 1)
                {
                    return shallow[0].FullName.Replace('\\', '/');
                }
            }

            // 2. Look for .exe files excluding auxiliary binaries
            var executables = fileEntries
                .Where(e => e.Name.EndsWith(ExeExtension, StringComparison.OrdinalIgnoreCase) && !IsAuxiliaryBinary(e.Name))
                .ToList();

            if (executables.Count == 1)
            {
                return executables[0].FullName.Replace('\\', '/');
            }

            if (executables.Count > 1)
            {
                var minDepth = executables.Min(e => e.FullName.Split('/', '\\').Length);
                var shallow = executables.Where(e => e.FullName.Split('/', '\\').Length == minDepth).ToList();
                if (shallow.Count == 1)
                {
                    return shallow[0].FullName.Replace('\\', '/');
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            // Expected archive read failure
        }

        return null;
    }

    /// <summary>
    /// Resolves an application bundle directory to its executable file, for launch targets
    /// that name the bundle root itself (locally added clients).
    /// </summary>
    /// <param name="bundleRoot">Absolute path of the <c>.app</c> directory.</param>
    /// <param name="cancellationToken">Cancels the bundle scan.</param>
    /// <returns>The absolute executable path, or <c>null</c> when unresolvable.</returns>
    public static string? ResolveBundleExecutableAbsolute(string bundleRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(bundleRoot)
            || !bundleRoot.EndsWith(AppExtension, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(bundleRoot))
        {
            return null;
        }

        var detection = ResolveBundleEntryPoint(bundleRoot, bundleRoot, cancellationToken);
        if (!detection.Success)
        {
            return null;
        }

        return Path.Combine(bundleRoot, detection.RelativePath!);
    }

    private static EntryPointResolution ResolveBundleEntryPoint(string payloadRoot, string bundleRoot, CancellationToken cancellationToken)
    {
        var declared = ReadBundleExecutable(bundleRoot, cancellationToken);
        if (declared is not null && TryResolveDeclaredBundleFile(payloadRoot, bundleRoot, declared, cancellationToken, out var declaredResolution))
        {
            return declaredResolution;
        }

        var natives = FindNativeBinaries(bundleRoot, cancellationToken)
            .Where(p => !ExecutableFileClassifier.IsLibraryFile(p) && IsUnderRoot(payloadRoot, p))
            .ToList();
        if (natives.Count == 1)
        {
            return EntryPointResolution.Resolved(
                ToRelativePath(payloadRoot, natives[0]),
                "only native binary in application bundle");
        }

        var known = FilterKnownGameBinaries(natives);
        if (known.Count == 1)
        {
            return EntryPointResolution.Resolved(
                ToRelativePath(payloadRoot, known[0]),
                "known game binary in application bundle");
        }

        return EntryPointResolution.Failed(
            natives.Count == 0
                ? $"Application bundle '{ToRelativePath(payloadRoot, bundleRoot)}' contains no native binary."
                : $"Application bundle '{ToRelativePath(payloadRoot, bundleRoot)}' contains {natives.Count} native binaries and declares no usable entry point.",
            natives.Select(n => ToRelativePath(payloadRoot, n)));
    }

    private static bool TryResolveDeclaredBundleFile(
        string payloadRoot,
        string bundleRoot,
        string declaredName,
        CancellationToken cancellationToken,
        out EntryPointResolution resolution)
    {
        if (string.IsNullOrWhiteSpace(declaredName)
            || Path.IsPathRooted(declaredName)
            || declaredName.Contains('/') || declaredName.Contains('\\')
            || declaredName.Contains("..", StringComparison.Ordinal))
        {
            resolution = default!;
            return false;
        }

        var declaredPath = FindFileInBundle(bundleRoot, declaredName, cancellationToken);
        if (declaredPath is null || !IsUnderRoot(payloadRoot, declaredPath))
        {
            resolution = default!;
            return false;
        }

        // A plist-declared script wins outright: that is the bundle's chosen launcher with
        // its environment (for example MoltenVK variables), not an auto-picked file.
        if (ExecutableFileClassifier.HasShebangHeader(declaredPath))
        {
            resolution = EntryPointResolution.Resolved(
                ToRelativePath(payloadRoot, declaredPath),
                "script declared by application bundle");
            return true;
        }

        // A plist-declared binary loses to a known game binary beside it: the plist may
        // name a nested launcher while the real client ships alongside it.
        var macOsDirectory = FindMacOsDirectory(bundleRoot, cancellationToken);
        if (macOsDirectory is not null)
        {
            var known = FilterKnownGameBinaries(GetFilesSafely(macOsDirectory))
                .Where(p => !p.Equals(declaredPath, PathHelper.PathComparison) && IsUnderRoot(payloadRoot, p))
                .ToList();
            if (known.Count == 1)
            {
                resolution = EntryPointResolution.Resolved(
                    ToRelativePath(payloadRoot, known[0]),
                    "known game binary preferred over bundle-declared binary");
                return true;
            }
        }

        resolution = EntryPointResolution.Resolved(
            ToRelativePath(payloadRoot, declaredPath),
            "binary declared by application bundle");
        return true;
    }

    private static EntryPointResolution ResolveFlatEntryPoint(string payloadRoot, CancellationToken cancellationToken)
    {
        // Shared libraries carry native magic too, so they are excluded: only files that
        // could actually be launched stay eligible for single-candidate resolution.
        var natives = FindNativeBinaries(payloadRoot, cancellationToken)
            .Where(p => !p.EndsWith(ExeExtension, StringComparison.OrdinalIgnoreCase)
                && !ExecutableFileClassifier.IsLibraryFile(p)
                && IsUnderRoot(payloadRoot, p))
            .ToList();

        var resolution = ResolveCandidateSet(payloadRoot, natives, "only native binary", "known primary game binary", "native binaries", preferKnown: true)
            ?? ResolveCandidateSet(payloadRoot, FindFlatpakPackages(payloadRoot, cancellationToken).Where(p => IsUnderRoot(payloadRoot, p)).ToList(), "only Flatpak bundle", string.Empty, "Flatpak bundles", preferKnown: false)
            ?? ResolveCandidateSet(payloadRoot, FindWindowsExecutables(payloadRoot, cancellationToken).Where(p => IsUnderRoot(payloadRoot, p)).ToList(), "only Windows executable", "known primary Windows executable", "Windows executables", preferKnown: true);

        return resolution ?? EntryPointResolution.Failed("Payload contains no launchable file.", Array.Empty<string>());
    }

    private static EntryPointResolution? ResolveCandidateSet(
        string payloadRoot,
        List<string> candidates,
        string singleReason,
        string knownReason,
        string pluralNoun,
        bool preferKnown)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return EntryPointResolution.Resolved(
                ToRelativePath(payloadRoot, candidates[0]),
                singleReason);
        }

        if (preferKnown)
        {
            var known = FilterKnownGameBinaries(candidates);
            if (known.Count == 1)
            {
                return EntryPointResolution.Resolved(
                    ToRelativePath(payloadRoot, known[0]),
                    knownReason);
            }

            if (known.Count > 1)
            {
                candidates = known;
            }
        }

        var nonAux = candidates.Where(c => !IsAuxiliaryBinary(c)).ToList();
        if (nonAux.Count == 1)
        {
            return EntryPointResolution.Resolved(
                ToRelativePath(payloadRoot, nonAux[0]),
                "primary candidate excluding auxiliary binaries");
        }

        if (nonAux.Count > 1)
        {
            candidates = nonAux;
        }

        var shallowest = FilterShallowestCandidates(payloadRoot, candidates);
        if (shallowest.Count == 1)
        {
            return EntryPointResolution.Resolved(
                ToRelativePath(payloadRoot, shallowest[0]),
                "shallowest candidate binary");
        }

        return EntryPointResolution.Failed(
            $"Payload contains {candidates.Count} {pluralNoun} and declares no entry point.",
            candidates.Select(c => ToRelativePath(payloadRoot, c)));
    }

    private static List<string> FilterShallowestCandidates(string payloadRoot, List<string> candidates)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var minDepth = candidates.Min(c => GetDepth(payloadRoot, c));
        return candidates.Where(c => GetDepth(payloadRoot, c) == minDepth).ToList();
    }

    private static int GetDepth(string payloadRoot, string path)
    {
        var rel = ToRelativePath(payloadRoot, path);
        return rel.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static bool IsAuxiliaryBinary(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        return name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("uninst", StringComparison.OrdinalIgnoreCase)
            || name.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || name.Contains("vcredist", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dxsetup", StringComparison.OrdinalIgnoreCase)
            || name.Contains("directx", StringComparison.OrdinalIgnoreCase)
            || name.Contains("crash", StringComparison.OrdinalIgnoreCase)
            || name.Contains("updater", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> FindBundleRoots(string payloadRoot, CancellationToken cancellationToken)
    {
        var roots = new List<string>();
        foreach (var directory in EnumerateDirectoriesSafely(payloadRoot, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directory.EndsWith(AppExtension, StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(directory);
            }
        }

        // A bundle nested inside another bundle (helper apps, Sparkle updaters, plug-ins)
        // is part of the outer bundle, not a second candidate.
        if (roots.Count > 1)
        {
            roots = roots
                .Where(candidate => !roots.Any(other =>
                    !ReferenceEquals(other, candidate)
                    && candidate.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        // Wrapper-stripping may remove the .app level itself, leaving a bare Contents tree.
        if (roots.Count == 0)
        {
            foreach (var directory in EnumerateDirectoriesSafely(payloadRoot, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFileName(directory).Equals(ContentsDirectoryName, StringComparison.OrdinalIgnoreCase)
                    && FindChildDirectory(directory, MacOsDirectoryName, cancellationToken) is not null)
                {
                    roots.Add(Path.GetDirectoryName(directory) ?? payloadRoot);
                }
            }
        }

        return roots;
    }

    private static string? ReadBundleExecutable(string bundleRoot, CancellationToken cancellationToken)
    {
        var plistPath = ResolveInfoPlist(bundleRoot, cancellationToken);
        if (plistPath is null)
        {
            return null;
        }

        try
        {
            var document = new XmlDocument();
            document.Load(plistPath);
            return ParseBundleExecutable(document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or ArgumentException)
        {
            return null;
        }
    }

    private static string? ResolveInfoPlist(string bundleRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var primary = Path.Combine(bundleRoot, ContentsDirectoryName, InfoPlistFileName);
        if (File.Exists(primary))
        {
            return primary;
        }

        var shallow = Path.Combine(bundleRoot, InfoPlistFileName);
        if (File.Exists(shallow))
        {
            return shallow;
        }

        try
        {
            return Directory.EnumerateFiles(bundleRoot, InfoPlistFileName, RecursiveOptions)
                .Where(p => !IsNestedBundlePlist(bundleRoot, p))
                .OrderBy(p => p.Length)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsNestedBundlePlist(string bundleRoot, string plistPath)
    {
        var relative = Path.GetRelativePath(bundleRoot, plistPath);
        var segments = relative.Split(DirectorySeparators);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];
            if (segment.Equals(FrameworksDirectoryName, StringComparison.OrdinalIgnoreCase)
                || segment.Equals(PlugInsDirectoryName, StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(AppExtension, StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith(FrameworkExtension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ParseBundleExecutable(XmlDocument document)
    {
        var root = document.DocumentElement;
        if (root is null)
        {
            return null;
        }

        var topDict = FindTopLevelDict(root);
        if (topDict is null)
        {
            return null;
        }

        // Top-level keys only: nested dicts (document types, plug-in metadata) must not
        // hijack the executable declaration.
        return ReadTopLevelString(topDict, BundleExecutableKey);
    }

    private static XmlNode? FindTopLevelDict(XmlNode root)
    {
        return root.ChildNodes.Cast<XmlNode>()
            .FirstOrDefault(child => child.NodeType == XmlNodeType.Element && child.Name.Equals("dict", StringComparison.Ordinal));
    }

    private static string? ReadTopLevelString(XmlNode topDict, string key)
    {
        var awaitingValue = false;
        foreach (XmlNode child in topDict.ChildNodes)
        {
            if (child.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (awaitingValue)
            {
                awaitingValue = false;
                if (child.Name.Equals("string", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(child.InnerText))
                {
                    return child.InnerText.Trim();
                }

                continue;
            }

            if (child.Name.Equals("key", StringComparison.Ordinal) && child.InnerText.Equals(key, StringComparison.Ordinal))
            {
                awaitingValue = true;
            }
        }

        return null;
    }

    private static string? FindFileInBundle(string bundleRoot, string fileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var macOsDirectory = FindMacOsDirectory(bundleRoot, cancellationToken);
        if (macOsDirectory is not null)
        {
            var direct = Path.Combine(macOsDirectory, fileName);
            if (File.Exists(direct))
            {
                return direct;
            }

            var match = GetFilesSafely(macOsDirectory)
                .Where(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Length)
                .ThenBy(f => f, StringComparer.Ordinal)
                .FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        try
        {
            return Directory.EnumerateFiles(bundleRoot, "*", RecursiveOptions)
                .Where(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Length)
                .ThenBy(f => f, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? FindMacOsDirectory(string bundleRoot, CancellationToken cancellationToken)
    {
        var direct = Path.Combine(bundleRoot, ContentsDirectoryName, MacOsDirectoryName);
        if (Directory.Exists(direct))
        {
            return direct;
        }

        return FindChildDirectory(bundleRoot, MacOsDirectoryName, cancellationToken);
    }

    private static string? FindChildDirectory(string parent, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Directory.EnumerateDirectories(parent, name, RecursiveOptions)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static List<string> FindNativeBinaries(string root, CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", RecursiveOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ExecutableFileClassifier.HasNativeExecutableMagicBytes(file))
                {
                    matches.Add(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }

        return matches;
    }

    private static List<string> FindFlatpakPackages(string root, CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", RecursiveOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.EndsWith(ContentFormatConstants.FlatpakExtension, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }

        return matches;
    }

    private static List<string> FindWindowsExecutables(string root, CancellationToken cancellationToken)
    {
        var matches = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", RecursiveOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(file);
                if (file.EndsWith(ExeExtension, StringComparison.OrdinalIgnoreCase)
                    || GameClientConstants.ValidGameExecutableNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
                    || ExecutableFileClassifier.DetectPlatform(file) == ExecutablePlatform.Windows)
                {
                    matches.Add(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }

        return matches;
    }

    private static List<string> FilterKnownGameBinaries(IEnumerable<string> paths)
    {
        return paths
            .Where(p => GameClientConstants.ValidGameExecutableNames.Contains(
                Path.GetFileName(p),
                StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static string[] GetFilesSafely(string directory)
    {
        try
        {
            return Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static List<string> EnumerateDirectoriesSafely(string root, CancellationToken cancellationToken)
    {
        var collected = new List<string>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "*", RecursiveOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();
                collected.Add(directory);
            }

            collected.Add(root);
            return collected;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [root];
        }
    }

    private static bool IsUnderRoot(string root, string path) => PathHelper.IsUnderRoot(root, path);

    private static string ToRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path);
    }
    private static bool IsSafeArchiveRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.EndsWith('/') || normalized.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = normalized.Split('/');
        return segments.All(s => !string.IsNullOrWhiteSpace(s) && s != "." && s != "..");
    }
}
