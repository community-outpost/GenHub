using GenHub.Core.Constants;
using GenHub.Core.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// </summary>
public static class GameClientEntryDetector
{
    private const string AppExtension = ".app";
    private const string ContentsDirectoryName = "Contents";
    private const string MacOsDirectoryName = "MacOS";
    private const string InfoPlistFileName = "Info.plist";
    private const string BundleExecutableKey = "CFBundleExecutable";
    private const string ExeExtension = ".exe";

    /// <summary>
    /// Detects the entry point of an extracted game client payload.
    /// </summary>
    /// <param name="extractedDirectory">The extracted payload root.</param>
    /// <returns>
    /// A resolution carrying the entry's path relative to <paramref name="extractedDirectory"/>,
    /// or a failure listing every candidate considered.
    /// </returns>
    public static EntryPointResolution DetectEntryPoint(string extractedDirectory)
    {
        if (string.IsNullOrWhiteSpace(extractedDirectory) || !Directory.Exists(extractedDirectory))
        {
            return EntryPointResolution.Failed(
                $"Payload directory '{extractedDirectory}' does not exist.",
                Array.Empty<string>());
        }

        var bundleRoots = FindBundleRoots(extractedDirectory);
        if (bundleRoots.Count > 1)
        {
            return EntryPointResolution.Failed(
                $"Payload contains {bundleRoots.Count} application bundles and declares no entry point.",
                bundleRoots.Select(b => ToRelativePath(extractedDirectory, b)));
        }

        if (bundleRoots.Count == 1)
        {
            return ResolveBundleEntryPoint(extractedDirectory, bundleRoots[0]);
        }

        return ResolveFlatEntryPoint(extractedDirectory);
    }

    private static EntryPointResolution ResolveBundleEntryPoint(string payloadRoot, string bundleRoot)
    {
        var declared = ReadBundleExecutable(bundleRoot);
        if (declared is not null && TryResolveDeclaredBundleFile(payloadRoot, bundleRoot, declared, out var declaredResolution))
        {
            return declaredResolution;
        }

        var natives = FindNativeBinaries(bundleRoot);
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
        out EntryPointResolution resolution)
    {
        var declaredPath = FindFileInBundle(bundleRoot, declaredName);
        if (declaredPath is null)
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
        var macOsDirectory = FindMacOsDirectory(bundleRoot);
        if (macOsDirectory is not null)
        {
            var known = FilterKnownGameBinaries(Directory.GetFiles(macOsDirectory))
                .Where(p => !p.Equals(declaredPath, StringComparison.OrdinalIgnoreCase))
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

    private static EntryPointResolution ResolveFlatEntryPoint(string payloadRoot)
    {
        var natives = FindNativeBinaries(payloadRoot)
            .Where(p => !p.EndsWith(ExeExtension, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (natives.Count == 1)
        {
            return EntryPointResolution.Resolved(
                ToRelativePath(payloadRoot, natives[0]),
                "only native binary");
        }

        if (natives.Count > 1)
        {
            var known = FilterKnownGameBinaries(natives);
            if (known.Count == 1)
            {
                return EntryPointResolution.Resolved(
                    ToRelativePath(payloadRoot, known[0]),
                    "known primary game binary");
            }

            return EntryPointResolution.Failed(
                $"Payload contains {natives.Count} native binaries and declares no entry point.",
                natives.Select(n => ToRelativePath(payloadRoot, n)));
        }

        var executables = FindWindowsExecutables(payloadRoot);
        if (executables.Count == 1)
        {
            return EntryPointResolution.Resolved(
                ToRelativePath(payloadRoot, executables[0]),
                "only Windows executable");
        }

        return EntryPointResolution.Failed(
            executables.Count == 0
                ? "Payload contains no launchable file."
                : $"Payload contains {executables.Count} Windows executables and declares no entry point.",
            executables.Select(e => ToRelativePath(payloadRoot, e)));
    }

    private static List<string> FindBundleRoots(string payloadRoot)
    {
        var roots = new List<string>();
        foreach (var directory in EnumerateDirectoriesSafely(payloadRoot))
        {
            if (directory.EndsWith(AppExtension, StringComparison.OrdinalIgnoreCase))
            {
                roots.Add(directory);
            }
        }

        // Wrapper-stripping may remove the .app level itself, leaving a bare Contents tree.
        if (roots.Count == 0)
        {
            foreach (var directory in EnumerateDirectoriesSafely(payloadRoot))
            {
                if (Path.GetFileName(directory).Equals(ContentsDirectoryName, StringComparison.OrdinalIgnoreCase)
                    && FindChildDirectory(directory, MacOsDirectoryName) is not null)
                {
                    var parent = Path.GetDirectoryName(directory);
                    roots.Add(parent ?? payloadRoot);
                }
            }
        }

        return roots;
    }

    private static string? ReadBundleExecutable(string bundleRoot)
    {
        var contents = FindChildDirectory(bundleRoot, ContentsDirectoryName);
        if (contents is null)
        {
            return null;
        }

        var plistPath = Path.Combine(contents, InfoPlistFileName);
        if (!File.Exists(plistPath))
        {
            var match = Directory.GetFiles(contents)
                .FirstOrDefault(f => Path.GetFileName(f).Equals(InfoPlistFileName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return null;
            }

            plistPath = match;
        }

        try
        {
            var document = new XmlDocument();
            document.Load(plistPath);
            var keys = document.GetElementsByTagName("key");
            foreach (XmlNode key in keys)
            {
                if (!key.InnerText.Equals(BundleExecutableKey, StringComparison.Ordinal))
                {
                    continue;
                }

                var sibling = key.NextSibling;
                while (sibling is not null && sibling.NodeType != XmlNodeType.Element)
                {
                    sibling = sibling.NextSibling;
                }

                if (sibling is not null
                    && sibling.Name.Equals("string", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(sibling.InnerText))
                {
                    return sibling.InnerText.Trim();
                }

                return null;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (XmlException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }

        return null;
    }

    private static string? FindFileInBundle(string bundleRoot, string fileName)
    {
        var macOsDirectory = FindMacOsDirectory(bundleRoot);
        if (macOsDirectory is not null)
        {
            var direct = Path.Combine(macOsDirectory, fileName);
            if (File.Exists(direct))
            {
                return direct;
            }

            var match = Directory.GetFiles(macOsDirectory)
                .FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        try
        {
            return Directory.EnumerateFiles(bundleRoot, "*", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? FindMacOsDirectory(string bundleRoot)
    {
        var contents = FindChildDirectory(bundleRoot, ContentsDirectoryName);
        return contents is null ? null : FindChildDirectory(contents, MacOsDirectoryName);
    }

    private static string? FindChildDirectory(string parent, string name)
    {
        var direct = Path.Combine(parent, name);
        if (Directory.Exists(direct))
        {
            return direct;
        }

        try
        {
            return Directory.EnumerateDirectories(parent)
                .FirstOrDefault(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static List<string> FindNativeBinaries(string root)
    {
        var natives = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (ExecutableFileClassifier.HasNativeExecutableMagicBytes(file))
                {
                    natives.Add(file);
                }
            }
        }
        catch (IOException)
        {
            // Best effort: return what was found before the enumeration failed.
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ArgumentException)
        {
        }

        return natives;
    }

    private static List<string> FindWindowsExecutables(string root)
    {
        var executables = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(ExeExtension, StringComparison.OrdinalIgnoreCase))
                {
                    executables.Add(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ArgumentException)
        {
        }

        return executables;
    }

    private static List<string> FilterKnownGameBinaries(IEnumerable<string> paths)
    {
        return paths
            .Where(p => GameClientConstants.ValidGameExecutableNames.Contains(
                Path.GetFileName(p),
                StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<string> EnumerateDirectoriesSafely(string root)
    {
        try
        {
            var collected = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).ToList();
            collected.Add(root);
            return collected;
        }
        catch (IOException)
        {
            return [root];
        }
        catch (UnauthorizedAccessException)
        {
            return [root];
        }
        catch (ArgumentException)
        {
            return [root];
        }
    }

    private static string ToRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path);
    }
}
