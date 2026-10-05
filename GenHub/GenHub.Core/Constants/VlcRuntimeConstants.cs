using System;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for native LibVLC runtime acquisition, verification, and filesystem layout.
/// </summary>
public static class VlcRuntimeConstants
{
    /// <summary>
    /// Package version for VideoLAN.LibVLC.Windows NuGet package.
    /// </summary>
    public const string PackageVersion = "3.0.24";

    /// <summary>
    /// Package ID for VideoLAN.LibVLC.Windows.
    /// </summary>
    public const string PackageId = "videolan.libvlc.windows";

    /// <summary>
    /// Expected SHA-512 digest of the official VideoLAN.LibVLC.Windows 3.0.24 NuGet package.
    /// </summary>
    public const string DefaultPackageSha512 = "1ADE0A9399D2355559EF3EDD1671C37F0A4B40C408A964C9E9FB211673FFD00DDEAD4741923ECBE4F2E65AB5719045528745859390978B012EF0C7BF7370DBEE";

    /// <summary>
    /// Environment variable to override the primary package download URL.
    /// </summary>
    public const string VlcPackageUrlEnvVar = "GENHUB_VLC_PACKAGE_URL";

    /// <summary>
    /// Environment variable to override the fallback package download URL.
    /// </summary>
    public const string VlcFallbackPackageUrlEnvVar = "GENHUB_VLC_FALLBACK_PACKAGE_URL";

    /// <summary>
    /// Environment variable to override the expected SHA-512 digest of the package.
    /// </summary>
    public const string VlcPackageSha512EnvVar = "GENHUB_VLC_PACKAGE_SHA512";

    /// <summary>
    /// Gets the default primary download URL for VideoLAN.LibVLC.Windows NuGet package from the NuGet v3 flat container feed.
    /// </summary>
    public static string DefaultPrimaryDownloadUrl => ApiConstants.GetNuGetPackageDownloadUrl(PackageId, PackageVersion);

    /// <summary>
    /// Default fallback download URL for VideoLAN.LibVLC.Windows NuGet package.
    /// </summary>
    [SuppressMessage("SonarQube", "S1075:URIs should not be hardcoded", Justification = "NuGet v2 package download URL fallback")]
    public const string DefaultFallbackDownloadUrl = "https://www.nuget.org/api/v2/package/VideoLAN.LibVLC.Windows/3.0.24";

    /// <summary>
    /// Gets the active primary download URL, honoring environment variable overrides.
    /// </summary>
    public static string PrimaryDownloadUrl =>
        Environment.GetEnvironmentVariable(VlcPackageUrlEnvVar) is { Length: > 0 } customUrl
            ? customUrl
            : DefaultPrimaryDownloadUrl;

    /// <summary>
    /// Gets the active fallback download URL, honoring environment variable overrides.
    /// </summary>
    public static string FallbackDownloadUrl =>
        Environment.GetEnvironmentVariable(VlcFallbackPackageUrlEnvVar) is { Length: > 0 } customUrl
            ? customUrl
            : DefaultFallbackDownloadUrl;

    /// <summary>
    /// Gets the expected SHA-512 digest, honoring environment variable overrides.
    /// </summary>
    public static string ExpectedSha512 =>
        Environment.GetEnvironmentVariable(VlcPackageSha512EnvVar) is { Length: > 0 } customHash
            ? customHash
            : DefaultPackageSha512;

    /// <summary>
    /// Subdirectory name under application data for runtimes.
    /// </summary>
    public const string RuntimesDirectoryName = "runtimes";

    /// <summary>
    /// Subdirectory name for LibVLC under runtimes.
    /// </summary>
    public const string VlcDirectoryName = "vlc";

    /// <summary>
    /// Directory name for Windows x64 binaries.
    /// </summary>
    public const string WinX64Directory = "win-x64";

    /// <summary>
    /// Directory name for Windows x86 binaries.
    /// </summary>
    public const string WinX86Directory = "win-x86";

    /// <summary>
    /// Package entry prefix for x64 architecture binaries.
    /// </summary>
    public const string X64EntryPrefix = "build/x64/";

    /// <summary>
    /// Package entry prefix for x86 architecture binaries.
    /// </summary>
    public const string X86EntryPrefix = "build/x86/";

    /// <summary>
    /// Package entry prefix for x64 C/C++ header files.
    /// </summary>
    public const string X64IncludePrefix = "build/x64/include/";

    /// <summary>
    /// Package entry prefix for x86 C/C++ header files.
    /// </summary>
    public const string X86IncludePrefix = "build/x86/include/";
}
