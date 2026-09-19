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

    /// <summary>Flatpak bundle. Needs the Flatpak container runtime.</summary>
    public const string FlatpakExtension = ".flatpak";

    /// <summary>Snap package. Needs the Snap container runtime.</summary>
    public const string SnapExtension = ".snap";

    /// <summary>Debian package. Needs the system package manager.</summary>
    public const string DebExtension = ".deb";

    /// <summary>RPM package. Needs the system package manager.</summary>
    public const string RpmExtension = ".rpm";

    /// <summary>
    /// Archive containers the pipeline extracts before detection: zip, 7z, rar, tar, and
    /// compressed tar variants. Bare single-file assets (native binaries, AppImages, scripts
    /// declared by a bundle) are handled alongside these, not through this list.
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
    /// Formats the pipeline refuses to turn into manifests. Each needs external tooling
    /// (disk mounting, installers, container runtimes) that does not belong in acquisition.
    /// </summary>
    public static readonly string[] GuidedRejectionExtensions =
    [
        DmgExtension,
        PkgExtension,
        FlatpakExtension,
        SnapExtension,
        DebExtension,
        RpmExtension,
    ];

    /// <summary>
    /// Actionable guidance per rejected format, naming the external step the user takes instead.
    /// </summary>
    /// <param name="extension">The rejected file extension, including the leading dot.</param>
    /// <returns>Guidance describing the external step the user takes instead.</returns>
    public static string GetRejectionGuidance(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            DmgExtension => "Mount the .dmg with hdiutil (or Finder), then add the game folder or .app bundle as local content.",
            PkgExtension => "Install the .pkg with the macOS installer first, then add the installed game folder as local content.",
            FlatpakExtension => "Install the Flatpak with flatpak(1) outside GenHub; containerized apps cannot be materialized into a workspace.",
            SnapExtension => "Install the snap with snapd outside GenHub; containerized apps cannot be materialized into a workspace.",
            DebExtension => "Install the .deb with your system package manager first, then add the installed game folder as local content.",
            RpmExtension => "Install the .rpm with your system package manager first, then add the installed game folder as local content.",
            _ => "This format needs external tooling that GenHub does not run; install it first, then add the installed files as local content.",
        };
    }
}
