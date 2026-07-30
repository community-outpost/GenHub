using System;
using System.Buffers.Binary;
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
    /// Bytes needed to recognise every supported magic number, including the second
    /// 32-bit word used to tell a Mach-O universal binary from a Java class file.
    /// </summary>
    private const int MagicHeaderLength = 8;

    /// <summary>
    /// A Mach-O universal (fat) header's second word is the architecture count, which is
    /// realistically single-digit. A Java class file shares the 0xCAFEBABE magic, but its
    /// second word encodes the class-file version, which is at least 45 (Java 1.1).
    /// </summary>
    private const uint MaxPlausibleFatArchCount = 30;

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
    /// Determines whether a file needs the Unix execute bit to be runnable, from its
    /// name alone.
    /// <para>
    /// This is what <c>ManifestFile.IsExecutable</c> means. It is a permission fact, not
    /// a statement about which file the profile launches.
    /// </para>
    /// <para>
    /// This is a compatibility heuristic for metadata-only contexts — remote release
    /// asset names, manifests whose content has not been acquired — where extensionless
    /// is assumed to mean native binary because that is the shape of a Mach-O or ELF
    /// game client. It is not content classification: whenever the file is on disk,
    /// call <see cref="RequiresExecutePermission(string, string?)"/> so the answer
    /// comes from the file's magic bytes instead.
    /// </para>
    /// </summary>
    /// <param name="path">A file name or relative path. Not required to exist on disk.</param>
    /// <returns><c>true</c> when the file should be marked executable.</returns>
    public static bool RequiresExecutePermissionFromName(string path)
        => RequiresExecutePermission(path, absolutePath: null);

    /// <summary>
    /// Determines whether a file needs the Unix execute bit, sniffing the file header
    /// to classify extensionless files by content rather than by name.
    /// <para>
    /// An extensionless <c>README</c> or <c>LICENSE</c> is neither a native binary nor a
    /// shebang script, and only the file's first bytes can tell these cases apart.
    /// Extension-based classification is unchanged: libraries stay non-executable and
    /// known runnable extensions stay executable, whatever the content says.
    /// </para>
    /// </summary>
    /// <param name="path">A file name or relative path.</param>
    /// <param name="absolutePath">
    /// The file's location on disk, when it exists there; <c>null</c> falls back to
    /// name-only classification. An extensionless file that cannot be read, or whose
    /// header is neither native executable magic nor a shebang, is not executable.
    /// </param>
    /// <returns><c>true</c> when the file should be marked executable.</returns>
    public static bool RequiresExecutePermission(string path, string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);

        // Extensionless: the shape of a native binary, but also of a README. Content is
        // the only reliable way to tell them apart, so use it whenever we have it.
        if (string.IsNullOrEmpty(extension))
        {
            return absolutePath is null || HasExecutePermissionHeader(absolutePath);
        }

        if (MatchesAny(extension, LibraryExtensions))
        {
            return false;
        }

        return MatchesAny(extension, RunnableExtensions);
    }

    /// <summary>
    /// Determines whether a file could be the launch target when a manifest declares no
    /// explicit entry point, from its name alone.
    /// <para>
    /// This exists only to keep manifests written before entry points were declarable
    /// working. New content should declare its entry point rather than rely on this.
    /// </para>
    /// <para>
    /// This is a compatibility heuristic for metadata-only contexts — remote release
    /// asset names, manifests whose content has not been acquired. It is not content
    /// classification: whenever the file is on disk, call
    /// <see cref="IsLegacyLaunchCandidate(string, string?)"/> so extensionless files
    /// are judged by their magic bytes instead.
    /// </para>
    /// </summary>
    /// <param name="path">A file name or relative path.</param>
    /// <returns><c>true</c> when the file is a plausible legacy launch target.</returns>
    public static bool IsLegacyLaunchCandidateFromName(string path)
        => IsLegacyLaunchCandidate(path, absolutePath: null);

    /// <summary>
    /// Determines whether a file could be the launch target when a manifest declares no
    /// explicit entry point, sniffing magic bytes to classify extensionless files by
    /// content rather than by name.
    /// </summary>
    /// <param name="path">A file name or relative path.</param>
    /// <param name="absolutePath">
    /// The file's location on disk, when it exists there; <c>null</c> falls back to
    /// name-only classification.
    /// </param>
    /// <returns><c>true</c> when the file is a plausible legacy launch target.</returns>
    public static bool IsLegacyLaunchCandidate(string path, string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);

        // Extensionless native binaries and Windows executables only. Notably not .dat:
        // the Steam layout launches game.dat through a proxy, but that is a launch
        // *strategy* chosen by the Steam integration, not a property of the file.
        if (string.IsNullOrEmpty(extension))
        {
            return absolutePath is null || HasExecutableMagicBytes(absolutePath);
        }

        return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the file at <paramref name="absolutePath"/> starts with the
    /// magic bytes of a native executable format. Reads at most
    /// <see cref="MagicHeaderLength"/> bytes; never loads the file.
    /// </summary>
    /// <param name="absolutePath">The file to sniff.</param>
    /// <returns>
    /// <c>true</c> when the header matches a known executable format; <c>false</c> for
    /// any other content and for files that are missing, too short, or unreadable.
    /// </returns>
    public static bool HasExecutableMagicBytes(string absolutePath)
    {
        Span<byte> header = stackalloc byte[MagicHeaderLength];

        return TryReadHeader(absolutePath, header, out var read)
            && HasExecutableMagicBytes(header[..read]);
    }

    /// <summary>
    /// Determines whether <paramref name="header"/> starts with the magic bytes of a
    /// native executable format: MZ (Windows PE), ELF (Linux), or Mach-O (macOS), in
    /// thin and universal flavours and both byte orders.
    /// </summary>
    /// <param name="header">The first bytes of a file; <see cref="MagicHeaderLength"/> suffice.</param>
    /// <returns><c>true</c> when the header matches a known executable format.</returns>
    public static bool HasExecutableMagicBytes(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
        {
            return false;
        }

        // MZ: DOS/PE. Two bytes of magic, but anything shorter than four bytes cannot
        // be a real executable of any kind, which the length gate above enforces.
        if (header[0] == 0x4D && header[1] == 0x5A)
        {
            return true;
        }

        // ELF: 0x7F 'E' 'L' 'F'.
        if (header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F')
        {
            return true;
        }

        var magic = BinaryPrimitives.ReadUInt32BigEndian(header);

        // Mach-O thin: MH_MAGIC / MH_MAGIC_64 and their byte-swapped forms.
        if (magic is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE)
        {
            return true;
        }

        // Mach-O universal (fat), 32-bit (FAT_MAGIC) and 64-bit (FAT_MAGIC_64) headers.
        // Java class files share 0xCAFEBABE, so require the second word: a fat header's
        // is the architecture count (tiny), a class file's is the class-file version
        // (>= 45). The byte-swapped magics store the count byte-swapped as well.
        if (magic is 0xCAFEBABE or 0xCAFEBABF && header.Length >= MagicHeaderLength)
        {
            return BinaryPrimitives.ReadUInt32BigEndian(header[4..]) < MaxPlausibleFatArchCount;
        }

        if (magic is 0xBEBAFECA or 0xBFBAFECA && header.Length >= MagicHeaderLength)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) < MaxPlausibleFatArchCount;
        }

        return false;
    }

    private static bool HasExecutePermissionHeader(string absolutePath)
    {
        Span<byte> header = stackalloc byte[MagicHeaderLength];

        if (!TryReadHeader(absolutePath, header, out var read))
        {
            return false;
        }

        var fileHeader = header[..read];

        // A shebang is a Unix permission fact, not native executable magic. Keep it out
        // of HasExecutableMagicBytes so scripts never become legacy launch candidates.
        return HasExecutableMagicBytes(fileHeader)
            || fileHeader is [0x23, 0x21, ..];
    }

    private static bool TryReadHeader(string absolutePath, Span<byte> header, out int read)
    {
        try
        {
            using var stream = new FileStream(
                absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            read = stream.ReadAtLeast(header, MagicHeaderLength, throwOnEndOfStream: false);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            read = 0;
            return false;
        }
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
