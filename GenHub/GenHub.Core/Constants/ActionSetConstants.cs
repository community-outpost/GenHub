namespace GenHub.Core.Constants;

using System.Collections.Generic;
using System.IO;

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

        /// <summary>
        /// Gets the DXSETUP.exe file name used for DirectX runtime installer.
        /// </summary>
        public const string DxSetupExe = "DXSETUP.exe";
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

        /// <summary>
        /// Gets the ScrollFactor key name for edge scrolling settings.
        /// </summary>
        public const string ScrollFactorKey = "ScrollFactor";
    }

    /// <summary>
    /// ActionSet category constants.
    /// </summary>
    public static class Categories
    {
        /// <summary>
        /// Gets the All category filter option.
        /// </summary>
        public const string All = "All";

        /// <summary>
        /// Gets the Core &amp; Stability category.
        /// </summary>
        public const string CoreAndStability = "Core & Stability";

        /// <summary>
        /// Gets the Compatibility category.
        /// </summary>
        public const string Compatibility = "Compatibility";

        /// <summary>
        /// Gets the Multiplayer category.
        /// </summary>
        public const string Multiplayer = "Multiplayer";

        /// <summary>
        /// Gets the Quality of Life category.
        /// </summary>
        public const string QualityOfLife = "Quality of Life";
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
        public const string RegistryUninstallKey = RegistryConstants.UninstallKeyPath;

        /// <summary>
        /// Gets the DisplayName value name in the registry.
        /// </summary>
        public const string DisplayNameValue = RegistryConstants.DisplayNameValueName;

        /// <summary>
        /// Gets the string to check for in DisplayName to identify Malwarebytes.
        /// </summary>
        public const string NameContains = "Malwarebytes";

        /// <summary>
        /// Gets the array of executable paths for Malwarebytes applications.
        /// </summary>
        public static readonly IReadOnlyList<string> ExecutablePaths =
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

        /// <summary>
        /// Gets the marker file name for remove read-only fix.
        /// </summary>
        public const string ReadOnlyFixMarker = ".gp_ro_fix";
    }

    /// <summary>
    /// Default serial keys used for fallback generation.
    /// </summary>
    public static class Serials
    {
        /// <summary>
        /// Default placeholder serial for Generals EA App installations.
        /// </summary>
        public const string DefaultEAAppGeneralsSerial = "GENS1234567890ABCDEF";

        /// <summary>
        /// Default placeholder serial for Zero Hour EA App installations.
        /// </summary>
        public const string DefaultEAAppZeroHourSerial = "ZH1234567890ABCDEFGH";
    }

    /// <summary>
    /// UI status badge colors.
    /// </summary>
    public static class StatusColors
    {
        /// <summary>Hex color for applied state.</summary>
        public const string Applied = "#28a745";

        /// <summary>Hex color for unapplied state.</summary>
        public const string Unapplied = "#ffc107";

        /// <summary>Hex color for not applicable state.</summary>
        public const string NotApplicable = "#6c757d";

        /// <summary>Hex color for checking state.</summary>
        public const string Checking = "#17a2b8";

        /// <summary>Hex color for error state.</summary>
        public const string Error = "#dc3545";

        /// <summary>Hex background color for applied state badge.</summary>
        public const string AppliedBackground = "#2228A745";

        /// <summary>Hex background color for unapplied state badge.</summary>
        public const string UnappliedBackground = "#22FFC107";

        /// <summary>Hex background color for not applicable state badge.</summary>
        public const string NotApplicableBackground = "#156c757d";

        /// <summary>Hex border color for applied state badge.</summary>
        public const string AppliedBorder = "#4428A745";

        /// <summary>Hex border color for unapplied state badge.</summary>
        public const string UnappliedBorder = "#44FFC107";

        /// <summary>Hex border color for not applicable state badge.</summary>
        public const string NotApplicableBorder = "#256c757d";
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

        /// <summary>
        /// Minimum file size for DirectX web setup installer (200 KB).
        /// </summary>
        public const long DirectXWebSetupMinSize = 200 * 1024;

        /// <summary>
        /// Minimum file size for DirectX runtime ZIP package (1 MB).
        /// </summary>
        public const long DirectXPackageMinSize = 1024 * 1024;

        /// <summary>
        /// Minimum file size for patch archives and installers (1 MB).
        /// </summary>
        public const long PatchMinSize = 1024 * 1024;

        /// <summary>
        /// Minimum file size for GenTool archive (200 KB).
        /// </summary>
        public const long GenToolMinSize = 200 * 1024;

        /// <summary>
        /// Minimum file size for addon packages like custom windows and high-definition icons (1 KB).
        /// </summary>
        public const long MinimumAddonPackageSizeBytes = 1024;

        /// <summary>
        /// Maximum file size for addon packages like custom windows and high-definition icons (200 MB).
        /// </summary>
        public const long MaximumAddonPackageSizeBytes = 200 * 1024 * 1024;
    }

    /// <summary>
    /// Security constants for digital signature and Authenticode publisher validation.
    /// </summary>
    public static class Security
    {
        /// <summary>
        /// Gets the expected Microsoft Corporation Authenticode publisher string.
        /// </summary>
        public const string MicrosoftPublisher = "Microsoft Corporation";

        /// <summary>
        /// Gets the expected Electronic Arts Authenticode publisher string.
        /// </summary>
        public const string ElectronicArtsPublisher = "Electronic Arts";

        /// <summary>
        /// Gets the pinned SHA-256 hash for the Generals 1.08 patch archive.
        /// </summary>
        public const string Generals108PatchSha256 = "265ff414850ef92e94828508f849a363c7fbe994d6994c6405e9eeaaa0f6b5c5";

        /// <summary>
        /// Gets the pinned SHA-256 hash for the DirectX runtime ZIP archive.
        /// </summary>
        public const string DirectXRuntimeZipSha256 = "6fcc7cd1be32422d07f022424412d6fe3141c6ba3845b855cb6f1b18f9c3a0a7";

        /// <summary>
        /// Gets the pinned SHA-256 hash for the GenTool archive package.
        /// </summary>
        public const string GenToolArchiveSha256 = "62bb0380ae14c570b6fad92b31784bec188dc22ac5ac9e11d3c524e08fa434e4";

        /// <summary>
        /// Gets the pinned SHA-256 hash for the GenTool d3d8.dll binary.
        /// </summary>
        public const string GenToolD3D8DllSha256 = "be5276180d04b3de9abd20aeaf2c1f65a2b65c800233ce49d5e77f1ab42441f7";

        /// <summary>
        /// Gets the pinned SHA-256 hash for the Expanded LAN Lobby / Custom Windows cbbs.dat package.
        /// </summary>
        public const string ExpandedLANLobbySha256 = "41f4c65c89bfae958d593a841b7f77aa6737cd12f810f5a3903a0a4cd6f7482d";

        /// <summary>
        /// Gets the pinned SHA-256 hash for the High-Definition Icons icon.dat package.
        /// </summary>
        public const string HDIconsSha256 = "68aedc84b0c4291dee7bdd079c551273e33cee4026ecc482ab48850cf99f7baa";
    }

    /// <summary>
    /// Constants for confirmation and notification dialogs.
    /// </summary>
    public static class Dialogs
    {
        /// <summary>
        /// Gets the title for the Apply All recommended fixes confirmation dialog.
        /// </summary>
        public const string ApplyAllConfirmationTitle = "Apply All Recommended Fixes";

        /// <summary>
        /// Gets the confirmation button text for the Apply All dialog.
        /// </summary>
        public const string ApplyAllConfirmButtonText = "Apply Fixes";

        /// <summary>
        /// Gets the cancel button text for the Apply All dialog.
        /// </summary>
        public const string ApplyAllCancelButtonText = "Cancel";
    }
}
