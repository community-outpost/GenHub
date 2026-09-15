using System.IO;

namespace GenHub.Core.Constants;

/// <summary>
/// The contract between GenHub and a non-Windows engine build for locating retail archives.
/// </summary>
/// <remarks>
/// A non-Windows engine reads <c>InstallPath</c> through <c>GetStringFromRegistry</c>, which
/// on these platforms consults these variables before anything else, then mounts
/// <c>*.big</c> from those roots in addition to the working directory. The names are
/// therefore an external contract: changing one silently stops the engine finding content,
/// with no error from either side.
/// <para>
/// Windows resolves install paths from the registry and does not read these, so nothing is
/// set — and nothing should be validated against them — on that platform.
/// </para>
/// </remarks>
public static class RetailArchiveConstants
{
    /// <summary>Environment variable naming the Zero Hour retail directory.</summary>
    public const string ZeroHourInstallPathVariable = "CNC_ZH_INSTALLPATH";

    /// <summary>Environment variable naming the Generals retail directory.</summary>
    public const string GeneralsInstallPathVariable = "CNC_GENERALS_INSTALLPATH";

    /// <summary>
    /// Stderr line prefix the engine writes when an archive's identifier does not match.
    /// The rest of the line is the archive path.
    /// </summary>
    /// <remarks>
    /// Fork-only, like <see cref="ArchiveMountFailedStderrPrefix"/>: emitted by
    /// <c>StdBIGFileSystem</c> on the bgfx fork and absent upstream on every platform,
    /// including <c>Win32BIGFileSystem</c>. Both sentinels are therefore strictly
    /// advisory — their absence means "this build does not emit one", never that the
    /// launch was healthy.
    /// </remarks>
    public const string ArchiveIdentifierMismatchStderrPrefix = "[ggc] archive identifier mismatch: ";

    /// <summary>
    /// Stderr line prefix the engine writes when an archive cannot be mounted at all.
    /// The rest of the line is the archive path.
    /// </summary>
    /// <remarks>
    /// See <see cref="ArchiveIdentifierMismatchStderrPrefix"/> for why matching this is
    /// advisory only.
    /// </remarks>
    public const string ArchiveMountFailedStderrPrefix = "[ggc] ARCHIVE MOUNT FAILED, contents unavailable this run: ";

    /// <summary>
    /// Search pattern for the archives the engine mounts from a retail root.
    /// </summary>
    /// <remarks>
    /// Used as an existence sentinel rather than matching a specific filename, which varies
    /// by localisation and version.
    /// </remarks>
    public const string ArchiveSearchPattern = "*.big";

    /// <summary>
    /// How <see cref="ArchiveSearchPattern"/> is matched within a retail root.
    /// </summary>
    /// <remarks>
    /// Case-insensitive because retail data copied from a disc or a Windows machine is
    /// frequently upper-cased, while the default glob is case-sensitive on Linux volumes and on
    /// case-sensitive APFS. <c>INIZH.BIG</c> would otherwise read as no archives at all and
    /// block a launch that would have worked — the opposite of what the check exists to do.
    /// <para>
    /// <see cref="EnumerationOptions.IgnoreInaccessible"/> is set back to <c>false</c> because
    /// it defaults to <c>true</c> here, unlike the <see cref="SearchOption"/> overload. Left at
    /// the default it turns an unreadable root into "no archives found", reporting a permission
    /// problem as missing content.
    /// </para>
    /// </remarks>
    public static readonly EnumerationOptions ArchiveSearch = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
    };

    /// <summary>
    /// Every retail archive root variable, Zero Hour first.
    /// </summary>
    public static readonly string[] InstallPathVariables =
    [
        ZeroHourInstallPathVariable,
        GeneralsInstallPathVariable,
    ];
}
