namespace GenHub.Core.Constants;

/// <summary>
/// Centralized constants for ActionSet fixes, registry keys, and file operations.
/// </summary>
public static class ActionSetConstants
{
    // RegistryKeys moved to GenHub.Core.Constants.RegistryConstants.cs

    /// <summary>
    /// File names and content.
    /// </summary>
    public static class FileNames
    {
        /// <summary>
        /// Gets the desktop.ini file name used for folder customization.
        /// </summary>
        public const string DesktopIni = "desktop.ini";

        /// <summary>
        /// Gets the Generals.exe file name.
        /// </summary>
        public const string GeneralsExe = "generals.exe";

        /// <summary>
        /// Gets the Game.dat file name.
        /// </summary>
        public const string GameDat = "Game.dat";

        /// <summary>
        /// Gets the game.exe file name, often used for Zero Hour.
        /// </summary>
        public const string GameExe = "game.exe"; // Often used for ZH
    }

    /// <summary>
    /// Initialization file sections and keys.
    /// </summary>
    public static class IniFiles
    {
        // Sections

        /// <summary>
        /// Gets the [.ShellClassInfo] section name for desktop.ini files.
        /// </summary>
        public const string ShellClassInfoSection = "[.ShellClassInfo]";

        /// <summary>
        /// Gets the TheSuperHackers section name for Options.ini files.
        /// </summary>
        public const string TheSuperHackersSection = "TheSuperHackers";

        // Keys

        /// <summary>
        /// Gets the ThisPCPolicy key name used to disable OneDrive sync.
        /// </summary>
        public const string ThisPCPolicyKey = "ThisPCPolicy";

        /// <summary>
        /// Gets the ThisPCPolicy value to disable OneDrive cloud sync.
        /// </summary>
        public const string ThisPCPolicyValue = "DisableCloudSync";

        /// <summary>
        /// Gets the ConfirmFileOp key name used in desktop.ini files.
        /// </summary>
        public const string ConfirmFileOpKey = "ConfirmFileOp";

        // TheSuperHackers keys

        /// <summary>
        /// Gets the ScrollEdgeZone key name for edge scrolling settings.
        /// </summary>
        public const string ScrollEdgeZoneKey = "ScrollEdgeZone";

        /// <summary>
        /// Gets the ScrollEdgeSpeed key name for edge scrolling settings.
        /// </summary>
        public const string ScrollEdgeSpeedKey = "ScrollEdgeSpeed";

        /// <summary>
        /// Gets the ScrollEdgeAcceleration key name for edge scrolling settings.
        /// </summary>
        public const string ScrollEdgeAccelerationKey = "ScrollEdgeAcceleration";
    }

    /// <summary>
    /// Firewall rule names and protocols.
    /// </summary>
    public static class FirewallRules
    {
        /// <summary>
        /// Gets the prefix used for firewall rule names for GenPatcher compatibility.
        /// </summary>
        public const string Prefix = "GP"; // Compatibility with GenPatcher

        /// <summary>
        /// Gets the firewall rule name for UDP port 16000.
        /// </summary>
        public const string PortRuleUdp16000 = "GP Open UDP Port 16000";

        /// <summary>
        /// Gets the firewall rule name for UDP port 16001.
        /// </summary>
        public const string PortRuleUdp16001 = "GP Open UDP Port 16001";

        /// <summary>
        /// Gets the firewall rule name for TCP port 16001.
        /// </summary>
        public const string PortRuleTcp16001 = "GP Open TCP Port 16001";

        /// <summary>
        /// Gets the firewall rule name for Generals.exe.
        /// </summary>
        public const string GeneralsRule = "GP Command & Conquer Generals";

        /// <summary>
        /// Gets the firewall rule name for Generals Game.dat.
        /// </summary>
        public const string GeneralsGameDatRule = "GP Command & Conquer Generals Game.dat";

        /// <summary>
        /// Gets the firewall rule name for Zero Hour.
        /// </summary>
        public const string ZeroHourRule = "GP Command & Conquer Generals Zero Hour";

        /// <summary>
        /// Gets the firewall rule name for Zero Hour Game.dat.
        /// </summary>
        public const string ZeroHourGameDatRule = "GP Command & Conquer Generals Zero Hour Game.dat";

        /// <summary>
        /// Gets the UDP protocol string.
        /// </summary>
        public const string ProtocolUdp = "UDP";

        /// <summary>
        /// Gets the TCP protocol string.
        /// </summary>
        public const string ProtocolTcp = "TCP";
    }

    /// <summary>
    /// Constants for Malwarebytes detection and paths.
    /// </summary>
    public static class Malwarebytes
    {
        /// <summary>
        /// Gets the registry uninstall key path for detecting Malwarebytes.
        /// </summary>
        public const string RegistryUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        /// <summary>
        /// Gets the DisplayName value name in the registry.
        /// </summary>
        public const string DisplayNameValue = "DisplayName";

        /// <summary>
        /// Gets the string to check for in DisplayName to identify Malwarebytes.
        /// </summary>
        public const string NameContains = "Malwarebytes";

        /// <summary>
        /// Gets the array of executable paths for Malwarebytes applications.
        /// </summary>
        public static readonly string[] ExecutablePaths =
        [
            Path.Combine("Malwarebytes", "Anti-Malware", "mbam.exe"),
            Path.Combine("Malwarebytes", "Anti-Malware", "mbamtray.exe")
        ];
    }

    /// <summary>
    /// File and directory paths used by ActionSets.
    /// </summary>
    public static class Paths
    {
        /// <summary>
        /// Gets the directory name for sub-action set markers.
        /// </summary>
        public const string SubActionSetMarkers = "sub_markers";
    }

    /// <summary>
    /// Validation constants for file operations.
    /// </summary>
    public static class Validation
    {
        /// <summary>
        /// Minimum file size for VCRedist installers (1000 KB).
        /// </summary>
        public const long VCRedistMinSize = 1000 * 1024;
    }
}
