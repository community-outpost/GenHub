namespace GenHub.Core.Models.Enums;

/// <summary>
/// Target operating system platform for an executable file, game client, or package.
/// </summary>
public enum ExecutablePlatform
{
    /// <summary>Unknown or unrecognized platform.</summary>
    Unknown = 0,

    /// <summary>Microsoft Windows platform (PE/MZ binaries, .exe).</summary>
    Windows = 1,

    /// <summary>Linux platform (ELF binaries, Flatpak packages, AppImages).</summary>
    Linux = 2,

    /// <summary>macOS platform (Mach-O binaries, .app bundles).</summary>
    MacOS = 3,
}
