using System;
using System.Collections.Generic;
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
    /// Search pattern for the archives the engine mounts from a retail root.
    /// </summary>
    /// <remarks>
    /// Used as an existence sentinel rather than matching a specific filename, which varies
    /// by localisation and version.
    /// </remarks>
    public const string ArchiveSearchPattern = "*.big";

    /// <summary>
    /// Filename suffix that marks an archive as Zero Hour content.
    /// </summary>
    /// <remarks>
    /// A retail fact, not an engine one: a retail Zero Hour installation ships its
    /// archives with this suffix (<c>INIZH.big</c>, <c>AudioZH.big</c>, …), verifiable
    /// against a real installation, so any such archive marks Zero Hour data. Deliberately
    /// not derived from any engine build's loading code, so the rule stays valid for a
    /// stock retail install with no community client. Compare case-insensitively — retail
    /// data copied from a disc or a Windows machine is frequently upper-cased.
    /// </remarks>
    public const string ZeroHourArchiveSuffix = "zh.big";

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

    /// <summary>
    /// The canonical archive filenames of a retail Generals installation.
    /// </summary>
    /// <remarks>
    /// A retail fact: these are the archives present in a retail Generals installation,
    /// verifiable against a real one. Deliberately not derived from any engine build's
    /// loading code, so the set stays valid for a stock retail install with no community
    /// client. Any one of them marks a directory as holding Generals data — localised SKUs
    /// vary in which language archives they carry, so requiring the full set would reject
    /// valid installs. The comparer is case-insensitive for the same reason as
    /// <see cref="ZeroHourArchiveSuffix"/>.
    /// </remarks>
    public static readonly IReadOnlySet<string> GeneralsArchiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "audio.big",
        "audioenglish.big",
        "english.big",
        "gensec.big",
        "ini.big",
        "maps.big",
        "music.big",
        "shaders.big",
        "speech.big",
        "speechenglish.big",
        "terrain.big",
        "textures.big",
        "w3d.big",
        "window.big",
    };
}
