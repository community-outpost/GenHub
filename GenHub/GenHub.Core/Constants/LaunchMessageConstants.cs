namespace GenHub.Core.Constants;

/// <summary>
/// Resource keys and English fallbacks for launch guard messages shared by the launch
/// runners and profile launch validation.
/// </summary>
public static class LaunchMessageConstants
{
    /// <summary>Resource key for the macOS-on-Windows guard message.</summary>
    public const string MacOSOnWindowsKey = "Launch.Compatibility.MacOSOnWindows";

    /// <summary>English fallback for the macOS-on-Windows guard message.</summary>
    public const string MacOSOnWindows = "Cannot launch macOS game client on Windows.";

    /// <summary>Resource key for the Linux-on-Windows guard message.</summary>
    public const string LinuxOnWindowsKey = "Launch.Compatibility.LinuxOnWindows";

    /// <summary>English fallback for the Linux-on-Windows guard message.</summary>
    public const string LinuxOnWindows = "Cannot launch Linux game client on Windows.";

    /// <summary>Resource key for the macOS-on-Linux guard message.</summary>
    public const string MacOSOnLinuxKey = "Launch.Compatibility.MacOSOnLinux";

    /// <summary>English fallback for the macOS-on-Linux guard message.</summary>
    public const string MacOSOnLinux = "Cannot launch macOS game client on Linux.";

    /// <summary>Resource key for the Linux-on-macOS guard message.</summary>
    public const string LinuxOnMacOSKey = "Launch.Compatibility.LinuxOnMacOS";

    /// <summary>English fallback for the Linux-on-macOS guard message.</summary>
    public const string LinuxOnMacOS = "Cannot launch Linux game client on macOS.";

    /// <summary>Resource key for the Flatpak install-then-run guidance message.</summary>
    public const string FlatpakRequiresInstallKey = "Launch.Flatpak.RequiresInstall";

    /// <summary>English fallback for the Flatpak guidance message ({0} bundle, {1} app id).</summary>
    public const string FlatpakRequiresInstall = "Cannot launch Flatpak bundle '{0}'. Install it with: flatpak install --user \"{0}\", then launch it with: flatpak run {1}.";

    /// <summary>Resource key for the Flatpak guidance message when the app id is unknown.</summary>
    public const string FlatpakRequiresInstallUnknownIdKey = "Launch.Flatpak.RequiresInstallUnknownId";

    /// <summary>English fallback for the Flatpak guidance message ({0} bundle).</summary>
    public const string FlatpakRequiresInstallUnknownId = "Cannot launch Flatpak bundle '{0}'. Install it with: flatpak install --user \"{0}\", then launch the application manually outside GenHub (GenHub cannot determine this bundle's application ID).";

    /// <summary>Resource key for the missing Flatpak CLI message.</summary>
    public const string FlatpakCliMissingKey = "Launch.Flatpak.CliMissing";

    /// <summary>English fallback for the missing Flatpak CLI message ({0} bundle).</summary>
    public const string FlatpakCliMissing = "Cannot launch Flatpak bundle '{0}': the Flatpak command line tools are not installed. Install Flatpak from https://flatpak.org/setup/ and try again.";

    /// <summary>Resource key for the Flatpak install failure message.</summary>
    public const string FlatpakInstallFailedKey = "Launch.Flatpak.InstallFailed";

    /// <summary>English fallback for the Flatpak install failure message ({0} bundle, {1} app id, {2} detail).</summary>
    public const string FlatpakInstallFailed = "Failed to install Flatpak bundle '{0}' ({1}): {2}";

    /// <summary>Resource key for the unresolvable-bundle guard message.</summary>
    public const string BundleUnresolvableKey = "Launch.Bundle.Unresolvable";

    /// <summary>English fallback for the unresolvable-bundle guard message ({0} bundle).</summary>
    public const string BundleUnresolvable = "Cannot launch application bundle '{0}': no executable could be resolved inside its Contents/MacOS folder.";

    /// <summary>Resource key for the Steam non-Windows executable message.</summary>
    public const string SteamNonWindowsExecutableKey = "Launch.Steam.NonWindowsExecutable";

    /// <summary>English fallback for the Steam non-Windows executable message ({0} executable filename).</summary>
    public const string SteamNonWindowsExecutable = "Steam Proxy Launcher cannot run non-Windows executable format '{0}'. Steam integration requires a Windows executable.";
}
