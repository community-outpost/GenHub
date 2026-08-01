using System;
using System.IO;

namespace GenHub.Core.Utilities;

/// <summary>
/// Single source of truth for what "executable" means when building a manifest.
/// <para>
/// Five call sites previously answered this independently and disagreed. Extensionless
/// files were classified executable by one and not by the other four, which matters
/// because a native Mach-O or ELF game binary has no extension:
/// </para>
/// <list type="bullet">
///   <item><description><c>ContentManifestBuilder</c>: .exe, .dll, .so, extensionless</description></item>
///   <item><description><c>GitHubInferenceHelper</c>: .exe, .dll, .sh, .bat, .so</description></item>
///   <item><description><c>ManifestGenerationService</c>: .exe, .dat</description></item>
///   <item><description><c>CommunityOutpostDeliverer</c>: .exe</description></item>
///   <item><description><c>FileTreeItem</c>: .exe</description></item>
/// </list>
/// <para>
/// It also conflated three separate questions: is this the launch target, is this
/// executable code, and does this file need the Unix execute bit. They have different
/// answers. A <c>.dylib</c> is executable code, is never a launch target, and is mapped
/// by dyld with read permission only — giving it +x is meaningless. A <c>.dat</c> is
/// data that the Steam layout happens to launch through, and is not code at all.
/// </para>
/// <para>
/// So this class answers exactly two questions, and the launch target is answered
/// elsewhere by an explicit declaration rather than inferred from a filename.
/// </para>
/// </summary>
public static class ExecutableFileClassifier
{
    /// <summary>
    /// Extensions for loadable code that is never itself launched and never needs the
    /// execute bit. Dynamic libraries are mapped by the loader, which requires read
    /// access only.
    /// </summary>
    private static readonly string[] LibraryExtensions = [".dll", ".so", ".dylib"];

    /// <summary>
    /// Extensions that are directly runnable and therefore need the execute bit on Unix.
    /// </summary>
    private static readonly string[] RunnableExtensions = [".exe", ".sh", ".command"];

    /// <summary>
    /// Determines whether a file needs the Unix execute bit to be runnable.
    /// <para>
    /// This is what <c>ManifestFile.IsExecutable</c> means. It is a permission fact, not
    /// a statement about which file the profile launches.
    /// </para>
    /// <para>
    /// Extensionless files count: that is the shape of a native Mach-O or ELF binary,
    /// and it is the case the previous inconsistency got wrong.
    /// </para>
    /// </summary>
    /// <param name="path">A file name or relative path. Not required to exist on disk.</param>
    /// <returns><c>true</c> when the file should be marked executable.</returns>
    public static bool RequiresExecutePermission(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);

        // Extensionless: a native binary. Directories and dotfiles are excluded by the
        // caller, which only ever passes real file entries.
        if (string.IsNullOrEmpty(extension))
        {
            return true;
        }

        if (MatchesAny(extension, LibraryExtensions))
        {
            return false;
        }

        return MatchesAny(extension, RunnableExtensions);
    }

    /// <summary>
    /// Determines whether a file could be the launch target when a manifest declares no
    /// explicit entry point.
    /// <para>
    /// This exists only to keep manifests written before entry points were declarable
    /// working. New content should declare its entry point rather than rely on this.
    /// </para>
    /// </summary>
    /// <param name="path">A file name or relative path.</param>
    /// <returns><c>true</c> when the file is a plausible legacy launch target.</returns>
    public static bool IsLegacyLaunchCandidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);

        // Extensionless native binaries and Windows executables only. Notably not .dat:
        // the Steam layout launches game.dat through a proxy, but that is a launch
        // *strategy* chosen by the Steam integration, not a property of the file.
        return string.IsNullOrEmpty(extension)
            || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAny(string extension, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
