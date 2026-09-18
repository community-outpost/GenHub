using System.IO;

namespace GenHub.Core.Constants;

/// <summary>
/// Directory name fragments used by the platform installation search path providers
/// to compose candidate game installation locations.
/// </summary>
public static class InstallationSearchPathConstants
{
    /// <summary>
    /// Linux-specific search path fragments.
    /// </summary>
    public static class Linux
    {
        /// <summary>Legacy Steam per-user configuration directory.</summary>
        public const string DotSteamDirectoryName = ".steam";

        /// <summary>Steam install directory under the legacy config path and the snap revision path.</summary>
        public const string SteamInstallDirectoryName = "steam";

        /// <summary>Legacy Steam runtime root directory.</summary>
        public const string SteamRootDirectoryName = "root";

        /// <summary>XDG local data parent directory.</summary>
        public const string XdgLocalDirectoryName = ".local";

        /// <summary>XDG shared data directory.</summary>
        public const string XdgShareDirectoryName = "share";

        /// <summary>Flatpak per-user application data root.</summary>
        public const string FlatpakVarDirectoryName = ".var";

        /// <summary>Flatpak application container directory.</summary>
        public const string FlatpakAppDirectoryName = "app";

        /// <summary>Steam Flatpak application identifier.</summary>
        public const string SteamFlatpakApplicationId = "com.valvesoftware.Steam";

        /// <summary>Flatpak writable data directory.</summary>
        public const string FlatpakDataDirectoryName = "data";

        /// <summary>Snap per-user data root.</summary>
        public const string SnapDirectoryName = "snap";

        /// <summary>User games directory used for non-Steam installations.</summary>
        public const string GamesDirectoryName = "Games";

        /// <summary>Default Wine prefix directory.</summary>
        public const string WinePrefixDirectoryName = ".wine";

        /// <summary>Windows C: drive mapping inside a Wine prefix.</summary>
        public const string WineDriveCDirectoryName = "drive_c";

        /// <summary>32-bit Windows program files directory inside a Wine prefix.</summary>
        public const string ProgramFilesX86DirectoryName = "Program Files (x86)";

        /// <summary>64-bit Windows program files directory inside a Wine prefix.</summary>
        public const string ProgramFilesDirectoryName = "Program Files";
    }

    /// <summary>
    /// macOS-specific search path fragments.
    /// </summary>
    public static class MacOS
    {
        /// <summary>Per-user library directory.</summary>
        public const string LibraryDirectoryName = "Library";

        /// <summary>Per-user application support directory.</summary>
        public const string ApplicationSupportDirectoryName = "Application Support";

        /// <summary>Per-user applications directory.</summary>
        public const string ApplicationsDirectoryName = "Applications";

        /// <summary>System-wide applications directory, composed with <see cref="Path"/> instead of a hardcoded separator.</summary>
        public static readonly string SystemApplicationsDirectory = Path.Combine(Path.DirectorySeparatorChar.ToString(), ApplicationsDirectoryName);
    }
}
