namespace GenHub.Core.Constants;

/// <summary>
/// Constants for launching Windows games through Wine on Linux and macOS.
/// </summary>
public static class WineConstants
{
    /// <summary>Primary Wine binary name resolved from PATH.</summary>
    public const string WineBinaryName = "wine";

    /// <summary>64-bit Wine binary name resolved from PATH.</summary>
    public const string Wine64BinaryName = "wine64";

    /// <summary>Environment variable selecting the Wine prefix.</summary>
    public const string PrefixEnvironmentVariable = "WINEPREFIX";

    /// <summary>Environment variable configuring Wine DLL overrides.</summary>
    public const string DllOverridesEnvironmentVariable = "WINEDLLOVERRIDES";

    /// <summary>Direct3D 8 wrapper DLL name as Wine matches it in WINEDLLOVERRIDES entries.</summary>
    public const string Direct3D8DllName = "d3d8";

    /// <summary>Default DLL override for Direct3D 8 wrappers (native, then builtin).</summary>
    public const string Direct3D8DllOverride = Direct3D8DllName + "=n,b";

    /// <summary>Process PATH environment variable used for binary lookup.</summary>
    public const string PathEnvironmentVariable = "PATH";

    /// <summary>Directory name of the GenHub-managed Wine prefix under the app data root.</summary>
    public const string ManagedPrefixDirectoryName = ".genhub-wine";

    /// <summary>Windows C: drive mapping inside a Wine prefix.</summary>
    public const string DriveCDirectoryName = "drive_c";

    /// <summary>User profiles directory inside a Wine prefix drive.</summary>
    public const string PrefixUsersDirectoryName = "users";

    /// <summary>Standard Windows Documents folder name inside a Wine prefix user profile ("Documents").</summary>
    public const string DocumentsDirectoryName = "Documents";

    /// <summary>Legacy Windows Documents folder name inside older Wine prefixes ("My Documents").</summary>
    public const string MyDocumentsDirectoryName = "My Documents";

    /// <summary>Fallback prefix user name when the login name is unusable as a directory name.</summary>
    public const string FallbackPrefixUserName = "user";

    /// <summary>Wine drive prefix mapped to the host filesystem root (Proton uses the same mapping).</summary>
    public const string HostRootDrivePrefix = "Z:";

    /// <summary>Executable extension the Wine runner wraps.</summary>
    public const string WindowsExecutableExtension = ".exe";

    /// <summary>CrossOver bundled Wine binary absolute path on macOS.</summary>
    public const string CrossOverWineBinaryPath = "/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine";
}
