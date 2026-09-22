namespace GenHub.Core.Constants;

/// <summary>
/// Constants for Flatpak application bundles, package handling, and CLI process execution.
/// </summary>
public static class FlatpakConstants
{
    /// <summary>Flatpak command line binary name.</summary>
    public const string BinaryName = "flatpak";

    /// <summary>Flatpak subcommand that runs an installed application.</summary>
    public const string RunCommand = "run";

    /// <summary>Flatpak subcommand that installs a bundle file.</summary>
    public const string InstallCommand = "install";

    /// <summary>Flatpak subcommand that reports whether an application is installed.</summary>
    public const string InfoCommand = "info";

    /// <summary>Flatpak flag selecting the per-user installation (no root required).</summary>
    public const string UserFlag = "--user";

    /// <summary>Flatpak flag answering yes to install prompts.</summary>
    public const string AssumeYesFlag = "-y";

    /// <summary>Flatpak run option prefix exposing a host directory inside the sandbox.</summary>
    public const string FilesystemOptionPrefix = "--filesystem=";

    /// <summary>Flatpak run option prefix setting an environment variable inside the sandbox.</summary>
    public const string EnvOptionPrefix = "--env=";

    /// <summary>Prefix of the ostree ref embedded in a Flatpak bundle header.</summary>
    public const string RefPrefix = "app/";

    /// <summary>Header bytes scanned for the embedded Flatpak ref.</summary>
    public const int HeaderScanSize = 65536;

    /// <summary>Sanity bound for an extracted Flatpak application ID.</summary>
    public const int MaxAppIdLength = 255;
}
