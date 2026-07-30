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
    /// Every retail archive root variable, Zero Hour first.
    /// </summary>
    public static readonly string[] InstallPathVariables =
    [
        ZeroHourInstallPathVariable,
        GeneralsInstallPathVariable,
    ];
}
