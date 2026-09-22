using GenHub.Core.Interfaces.Common;

namespace GenHub.Core.Constants;

/// <summary>
/// Describes which remote content formats the acquisition pipeline understands and which
/// ones it rejects with guidance. Single source of truth for the discovery filter and the
/// resolver guard, so both agree on what may become a manifest.
/// </summary>
public static class ContentFormatConstants
{
    /// <summary>macOS disk image. Needs hdiutil mounting outside the pipeline.</summary>
    public const string DmgExtension = ".dmg";

    /// <summary>macOS installer package. Needs the system installer outside the pipeline.</summary>
    public const string PkgExtension = ".pkg";

    /// <summary>
    /// Flatpak bundle. Understood as manifest content with the bundle file as the launch
    /// entry; GameProcessManager provisions the bundle (install) and runs it via the
    /// Flatpak CLI on Linux, while other platforms fail with install guidance.
    /// </summary>
    public const string FlatpakExtension = ".flatpak";

    /// <summary>Linux AppImage executable package.</summary>
    public const string AppImageExtension = ".appimage";

    /// <summary>Flatpak command line binary name.</summary>
    public const string FlatpakBinaryName = "flatpak";

    /// <summary>Flatpak subcommand that runs an installed application.</summary>
    public const string FlatpakRunCommand = "run";

    /// <summary>Flatpak subcommand that installs a bundle file.</summary>
    public const string FlatpakInstallCommand = "install";

    /// <summary>Flatpak subcommand that reports whether an application is installed.</summary>
    public const string FlatpakInfoCommand = "info";

    /// <summary>Flatpak flag selecting the per-user installation (no root required).</summary>
    public const string FlatpakUserFlag = "--user";

    /// <summary>Flatpak flag answering yes to install prompts.</summary>
    public const string FlatpakAssumeYesFlag = "-y";

    /// <summary>Flatpak run option prefix exposing a host directory inside the sandbox.</summary>
    public const string FlatpakFilesystemOptionPrefix = "--filesystem=";

    /// <summary>Flatpak run option prefix setting an environment variable inside the sandbox.</summary>
    public const string FlatpakEnvOptionPrefix = "--env=";

    /// <summary>macOS application bundle directory suffix.</summary>
    public const string MacAppBundleExtension = ".app";

    /// <summary>Snap package. Needs the Snap container runtime.</summary>
    public const string SnapExtension = ".snap";

    /// <summary>Debian package. Needs the system package manager.</summary>
    public const string DebExtension = ".deb";

    /// <summary>RPM package. Needs the system package manager.</summary>
    public const string RpmExtension = ".rpm";

    /// <summary>Windows installer package. Needs Windows or Wine msiexec outside the pipeline.</summary>
    public const string MsiExtension = ".msi";

    /// <summary>Windows app package. Needs Windows outside the pipeline.</summary>
    public const string MsixExtension = ".msix";

    /// <summary>Sub-marker for versioned shared object libraries (e.g. libfoo.so.2).</summary>
    public const string VersionedSharedLibraryMarker = ".so.";

    /// <summary>Prefix of the ostree ref embedded in a Flatpak bundle header.</summary>
    public const string FlatpakRefPrefix = "app/";

    /// <summary>Header bytes scanned for the embedded Flatpak ref.</summary>
    public const int FlatpakHeaderScanSize = 65536;

    /// <summary>Sanity bound for an extracted Flatpak application ID.</summary>
    public const int FlatpakMaxAppIdLength = 255;

    /// <summary>Resource key for the prefix of cannot-install-directly failure messages.</summary>
    public const string RejectionCannotInstallDirectlyKey = "ContentFormat.Rejection.CannotInstallDirectly";

    /// <summary>English fallback for the prefix of cannot-install-directly failure messages ({0} fileName, {1} guidance).</summary>
    public const string RejectionCannotInstallDirectlyFallback = "'{0}' cannot be installed directly. {1}";

    /// <summary>Resource key for .dmg rejection guidance.</summary>
    public const string RejectionDmgKey = "ContentFormat.Rejection.Dmg";

    /// <summary>English fallback for .dmg rejection guidance.</summary>
    public const string RejectionDmgFallback = "Mount the .dmg with hdiutil (or Finder), then add the game folder or .app bundle as local content.";

    /// <summary>Resource key for .pkg rejection guidance.</summary>
    public const string RejectionPkgKey = "ContentFormat.Rejection.Pkg";

    /// <summary>English fallback for .pkg rejection guidance.</summary>
    public const string RejectionPkgFallback = "Install the .pkg with the macOS installer first, then add the installed game folder as local content.";

    /// <summary>Resource key for .snap rejection guidance.</summary>
    public const string RejectionSnapKey = "ContentFormat.Rejection.Snap";

    /// <summary>English fallback for .snap rejection guidance.</summary>
    public const string RejectionSnapFallback = "Install the snap with snapd outside GenHub; containerized apps cannot be materialized into a workspace.";

    /// <summary>Resource key for .deb rejection guidance.</summary>
    public const string RejectionDebKey = "ContentFormat.Rejection.Deb";

    /// <summary>English fallback for .deb rejection guidance.</summary>
    public const string RejectionDebFallback = "Install the .deb with your system package manager first, then add the installed game folder as local content.";

    /// <summary>Resource key for .rpm rejection guidance.</summary>
    public const string RejectionRpmKey = "ContentFormat.Rejection.Rpm";

    /// <summary>English fallback for .rpm rejection guidance.</summary>
    public const string RejectionRpmFallback = "Install the .rpm with your system package manager first, then add the installed game folder as local content.";

    /// <summary>Resource key for .msi rejection guidance.</summary>
    public const string RejectionMsiKey = "ContentFormat.Rejection.Msi";

    /// <summary>English fallback for .msi rejection guidance.</summary>
    public const string RejectionMsiFallback = "Run the .msi installer on Windows (or with Wine msiexec) first, then add the installed game folder as local content.";

    /// <summary>Resource key for .msix rejection guidance.</summary>
    public const string RejectionMsixKey = "ContentFormat.Rejection.Msix";

    /// <summary>English fallback for .msix rejection guidance.</summary>
    public const string RejectionMsixFallback = "Install the .msix package on Windows first, then add the installed game folder as local content.";

    /// <summary>Resource key for default fallback rejection guidance.</summary>
    public const string RejectionDefaultKey = "ContentFormat.Rejection.Default";

    /// <summary>English fallback for default rejection guidance.</summary>
    public const string RejectionDefaultFallback = "This format needs external tooling that GenHub does not run; install it first, then add the installed files as local content.";

    /// <summary>
    /// Archive containers the pipeline extracts before detection: zip, 7z, rar, tar, and
    /// compressed tar variants. Bare single-file assets (native binaries, Flatpaks,
    /// AppImages, scripts declared by a bundle) are handled alongside these, not through
    /// this list.
    /// </summary>
    public static readonly string[] UnderstoodArchiveExtensions =
    [
        ".zip",
        ".7z",
        ".rar",
        ".tar",
        ".tgz",
        ".gz",
        ".bz2",
        ".xz",
    ];

    /// <summary>
    /// Single-file game-data extensions that are content on their own (a lone mod
    /// archive or texture), as opposed to release notes or documentation that merely
    /// share a release with real content.
    /// </summary>
    public static readonly string[] StandaloneContentExtensions =
    [
        ".big",
        ".csf",
        ".ini",
        ".w3d",
        ".dds",
        ".tga",
        ".zip",
    ];

    /// <summary>
    /// Formats the pipeline refuses to turn into manifests. Each needs external tooling
    /// (disk mounting, installers, system package managers) that does not belong in acquisition.
    /// </summary>
    public static readonly string[] GuidedRejectionExtensions =
    [
        DmgExtension,
        PkgExtension,
        SnapExtension,
        DebExtension,
        RpmExtension,
        MsiExtension,
        MsixExtension,
    ];

    /// <summary>
    /// Common documentation file names (without extensions or with doc extensions) that should
    /// not be treated as standalone content assets in metadata-only contexts.
    /// </summary>
    public static readonly string[] KnownDocumentationFileNames =
    [
        "license",
        "licence",
        "copying",
        "readme",
        "changelog",
        "changes",
        "authors",
        "contributing",
        "notice",
        "install",
        "history",
        "news",
        "todo",
        "security",
        "patch-notes",
        "release-notes",
    ];

    /// <summary>
    /// Gets the localization resource key for the given rejected format extension.
    /// </summary>
    /// <param name="extension">The rejected file extension, including the leading dot.</param>
    /// <returns>The resource key corresponding to the extension.</returns>
    public static string GetRejectionGuidanceKey(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            DmgExtension => RejectionDmgKey,
            PkgExtension => RejectionPkgKey,
            SnapExtension => RejectionSnapKey,
            DebExtension => RejectionDebKey,
            RpmExtension => RejectionRpmKey,
            MsiExtension => RejectionMsiKey,
            MsixExtension => RejectionMsixKey,
            _ => RejectionDefaultKey,
        };
    }

    /// <summary>
    /// Actionable guidance per rejected format, naming the external step the user takes instead.
    /// </summary>
    /// <param name="extension">The rejected file extension, including the leading dot.</param>
    /// <param name="localizationService">Optional localization service for user-facing text.</param>
    /// <returns>Guidance describing the external step the user takes instead.</returns>
    public static string GetRejectionGuidance(string extension, ILocalizationService? localizationService = null)
    {
        var normalized = extension?.ToLowerInvariant() ?? string.Empty;
        var key = GetRejectionGuidanceKey(normalized);
        if (localizationService?.TryGetString(key, out var localized) == true)
        {
            return localized;
        }

        return normalized switch
        {
            DmgExtension => RejectionDmgFallback,
            PkgExtension => RejectionPkgFallback,
            SnapExtension => RejectionSnapFallback,
            DebExtension => RejectionDebFallback,
            RpmExtension => RejectionRpmFallback,
            MsiExtension => RejectionMsiFallback,
            MsixExtension => RejectionMsixFallback,
            _ => RejectionDefaultFallback,
        };
    }
}
