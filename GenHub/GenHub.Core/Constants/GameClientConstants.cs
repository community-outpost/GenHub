using System.Collections.Generic;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants related to game client detection and management.
/// </summary>
public static class GameClientConstants
{
    // ===== Game Executables =====

    /// <summary>Generals executable filename.</summary>
    public const string GeneralsExecutable = "generals.exe";

    /// <summary>Zero Hour executable filename (EA App/Retail installations).</summary>
    /// <remarks>Both Generals and Zero Hour ship as <c>generals.exe</c>; this intentionally
    /// shares its value with <see cref="GeneralsExecutable"/> and is covered by
    /// <see cref="ValidGameExecutableNames"/> through that entry.</remarks>
    public const string ZeroHourExecutable = "generals.exe";

    /// <summary>Game engine executable filename.</summary>
    public const string GameExecutable = "game.exe";

    /// <summary>Child game process name (without extension).</summary>
    public const string GameProcessName = "game";

    /// <summary>Steam game.dat executable (primary for Steam installations, avoids launcher stubs).</summary>
    public const string SteamGameDatExecutable = "game.dat";

    /// <summary>Contra modded client executable filename.</summary>
    public const string ContraExecutable = "generals.ctr";

    /// <summary>Unix Zero Hour client executable filename (extensionless Mach-O or ELF binary).</summary>
    public const string GeneralsOnlineUnixExecutable = "GeneralsOnlineZH";

    // ===== Engine Launch Arguments =====

    /// <summary>SAGE engine command-line argument overriding the horizontal resolution.</summary>
    public const string XResolutionArgument = "-xres";

    /// <summary>SAGE engine command-line argument overriding the vertical resolution.</summary>
    public const string YResolutionArgument = "-yres";

    /// <summary>SAGE engine command-line argument requesting windowed mode.</summary>
    public const string WindowedArgument = "-win";

    // ===== Launcher Wrapper & Stub Hashes =====

    /// <summary>SHA-256 hash of modern Steam/EA App Zero Hour generals.exe launcher stub.</summary>
    public const string ModernLauncherStubSha256 = "FF6F78211A014100D8EF6B08BC2F8EDD3D55E99E872DFDB5371776FC5A5D02CE";

    /// <summary>SHA-256 hash of EA App Generals 1.08 generals.exe launcher wrapper.</summary>
    public const string EaAppGeneralsLauncherWrapperSha256 = "8DDE6C990280AC44B4629A664B24BBAF226E629E9C7700234010F198783B6674";

    // ===== SuperHackers Client Detection =====

    /// <summary>SuperHackers Generals executable filename.</summary>
    public const string SuperHackersGeneralsExecutable = "generalsv.exe";

    /// <summary>Super Hackers Zero Hour executable filename.</summary>
    public const string SuperHackersZeroHourExecutable = "generalszh.exe";

    /// <summary>Display name for SuperHackers Generals client.</summary>
    public const string SuperHackersGeneralsDisplayName = "SuperHackers Generals";

    /// <summary>Display name for SuperHackers Zero Hour client.</summary>
    public const string SuperHackersZeroHourDisplayName = "SuperHackers Zero Hour";

    // ===== Game Directory Names =====

    /// <summary>Standard Generals installation directory name.</summary>
    public const string GeneralsDirectoryName = "Command and Conquer Generals";

    /// <summary>Standard Zero Hour installation directory name.</summary>
    public const string ZeroHourDirectoryName = "Command and Conquer Generals Zero Hour";

    /// <summary>Zero Hour directory name with ampersand and hyphen (Steam standard).</summary>
    public const string ZeroHourDirectoryNameAmpersandHyphen = "Command & Conquer Generals - Zero Hour";

    /// <summary>Zero Hour directory name with colon variant.</summary>
    public const string ZeroHourDirectoryNameColonVariant = "Command & Conquer: Generals - Zero Hour";

    /// <summary>Zero Hour directory name abbreviated form.</summary>
    public const string ZeroHourDirectoryNameAbbreviated = "C&C Generals Zero Hour";

    /// <summary>EA Games parent directory name.</summary>
    public const string EaGamesParentDirectoryName = "EA Games";

    /// <summary>Electronic Arts parent directory name.</summary>
    public const string ElectronicArtsParentDirectoryName = "Electronic Arts";

    /// <summary>Generals subdirectory name.</summary>
    public const string GeneralsSubdirectoryName = "Generals";

    /// <summary>Zero Hour subdirectory name.</summary>
    public const string ZeroHourSubdirectoryName = "ZeroHour";

    /// <summary>Standard retail Generals directory name.</summary>
    public const string GeneralsRetailDirectoryName = "Command & Conquer Generals";

    /// <summary>Standard retail Zero Hour directory name.</summary>
    public const string ZeroHourRetailDirectoryName = "Command & Conquer Generals Zero Hour";

    /// <summary>Directory marker for Zero Hour's Generals installation link.</summary>
    public const string ZhGeneralsDirectory = "ZH_Generals";

    /// <summary>Directory containing activation DLLs for modern Steam and EA App releases.</summary>
    public const string CoreDirectory = "Core";

    /// <summary>Directory marker used by Steam DRM wrapper installations.</summary>
    public const string SteamDrmMarkerDirectory = "__Installer";

    // ===== Core Game Archives =====

    /// <summary>Primary Zero Hour INI archive filename.</summary>
    public const string ZeroHourIniBig = "INIZH.big";

    /// <summary>Primary Zero Hour Patch archive filename.</summary>
    public const string ZeroHourPatchBig = "PatchZH.big";

    /// <summary>Primary Generals Vanilla INI archive filename.</summary>
    public const string GeneralsIniBig = "INI.big";

    /// <summary>Primary Generals Vanilla Patch archive filename.</summary>
    public const string GeneralsPatchBig = "Patch.big";

    /// <summary>Generals Vanilla security archive filename.</summary>
    public const string GeneralsSecurityBig = "gensec.big";

    /// <summary>Zero Hour archive extension suffix.</summary>
    public const string ZeroHourArchiveExtensionSuffix = "ZH.big";

    // ===== GeneralsOnline Client Detection =====

    /// <summary>GeneralsOnline 60Hz client executable name.</summary>
    public const string GeneralsOnline60HzExecutable = "generalsonlinezh_60.exe";

    /// <summary>GeneralsOnline default client executable name.</summary>
    public const string GeneralsOnlineDefaultExecutable = "generalsonlinezh.exe";

    /// <summary>
    /// Easy Anti-Cheat bootstrapper shipped since GeneralsOnline 060526_QFE1. It launches the
    /// binary named by <c>EasyAntiCheat/Settings.json</c> and is the supported launch target.
    /// </summary>
    public const string GeneralsOnlineEacLauncherExecutable = "EAC_LaunchGeneralsOnline.exe";

    /// <summary>Epic Online Services Easy Anti-Cheat installer shipped in the GeneralsOnline portable.</summary>
    public const string GeneralsOnlineEacSetupExecutable = "EasyAntiCheat_EOS_Setup.exe";

    /// <summary>Display name for GeneralsOnline 60Hz variant.</summary>
    public const string GeneralsOnline60HzDisplayName = "GeneralsOnline 60Hz";

    /// <summary>Default display name for GeneralsOnline variants.</summary>
    public const string GeneralsOnlineDefaultDisplayName = "GeneralsOnline";

    // ===== Dependency Names =====

    /// <summary>Name for Zero Hour installation dependency requirement.</summary>
    public const string ZeroHourInstallationDependencyName = "Zero Hour Installation (Required)";

    /// <summary>Name for Generals installation dependency requirement.</summary>
    public const string GeneralsInstallationDependencyName = "Generals Installation (Required)";

    // ===== Version Strings =====

    /// <summary>Version string used for automatically detected clients.</summary>
    public const string AutoDetectedVersion = GameClientConstants.UnknownVersion;

    /// <summary>
    /// Version sentinel used for clients that update themselves, so no fixed version can be read.
    /// </summary>
    public const string AutoUpdatedVersion = "Auto-Updated";

    /// <summary>Version string used for unknown/unrecognized clients.</summary>
    public const string UnknownVersion = "Unknown";

    // ===== Steam Latest Versions =====

    /// <summary>Latest Steam version for Command &amp; Conquer Generals.</summary>
    public const string LatestSteamGeneralsVersion = "1.09";

    /// <summary>Latest Steam version for Command &amp; Conquer Generals Zero Hour.</summary>
    public const string LatestSteamZeroHourVersion = "1.05";

    // ===== Game Display Names =====

    /// <summary>
    /// Canonical full display name for Command &amp; Conquer: Generals.
    /// </summary>
    public const string GeneralsFullName = "Command & Conquer: Generals";

    /// <summary>
    /// Canonical short display name for Command &amp; Conquer: Generals.
    /// </summary>
    public const string GeneralsShortName = "Generals";

    /// <summary>
    /// Canonical full display name for Command &amp; Conquer: Generals Zero Hour.
    /// </summary>
    public const string ZeroHourFullName = "Command & Conquer: Generals Zero Hour";

    /// <summary>
    /// Canonical short display name for Command &amp; Conquer: Generals Zero Hour.
    /// </summary>
    public const string ZeroHourShortName = "Zero Hour";

    /// <summary>Display name for Windows platform.</summary>
    public const string PlatformWindowsDisplayName = "Windows";

    /// <summary>Display name for Linux platform.</summary>
    public const string PlatformLinuxDisplayName = "Linux";

    /// <summary>Display name for macOS platform.</summary>
    public const string PlatformMacOSDisplayName = "macOS";

    /// <summary>Display name for cross-platform/generic platform.</summary>
    public const string PlatformCrossPlatformDisplayName = "Cross-Platform";

    /// <summary>BrowserEngine.dll filename.</summary>
    public const string BrowserEngineDll = "BrowserEngine.dll";

    /// <summary>BrowserEngine.dll backup filename.</summary>
    public const string BrowserEngineDllBak = "BrowserEngine.dll.bak";

    /// <summary>dbghelp.dll filename.</summary>
    public const string DbgHelpDll = "dbghelp.dll";

    /// <summary>dbghelp.dll backup filename.</summary>
    public const string DbgHelpDllBak = "dbghelp.dll.bak";

    /// <summary>Direct3D 8 wrapper DLL filename.</summary>
    public const string Direct3D8WrapperDll = "d3d8.dll";

    /// <summary>GenTool updater executable filename.</summary>
    public const string GenToolUpdaterExe = "GenToolUpdater.exe";

    /// <summary>Corrupt MapCache backup file extension.</summary>
    public const string CorruptMapCacheBackupExtension = ".corrupt.bak";

    /// <summary>
    /// DLLs required for standard game installations.
    /// </summary>
    public static readonly string[] RequiredDlls =
    [
        "steam_api.dll",      // Steam integration
        "binkw32.dll",        // Bink video codec
        "mss32.dll",          // Miles Sound System
        "eauninstall.dll",    // EA App integration
        "P2XDLL.DLL",         // EA/Steam wrapper DLL
        "patchw32.dll",       // Update/Patch engine DLL
        "dbghelp.dll",        // Debugging help (often included)
    ];

    /// <summary>
    /// DLLs specific to GeneralsOnline installations.
    /// </summary>
    public static readonly string[] GeneralsOnlineDlls =
    [

        // Core runtime DLLs (required for GeneralsOnline client)
        "abseil_dll.dll",          // Abseil C++ library for networking
        "GameNetworkingSockets.dll", // Valve networking library
        "libcrypto-3.dll",         // OpenSSL crypto library
        "libcurl.dll",             // HTTP/HTTPS networking
        "libprotobuf.dll",         // Protocol buffers serialization
        "libssl-3.dll",            // OpenSSL SSL/TLS library
        "sentry.dll",              // Error reporting and crash analytics
        "zlib1.dll",               // Compression library

        "steam_api.dll",           // Steam integration (optional)
        "binkw32.dll",             // Bink video codec
        "mss32.dll",               // Miles Sound System
        "wsock32.dll",             // Network socket library
    ];

    /// <summary>Common registry value names for installation paths.</summary>
    public static readonly string[] InstallationPathRegistryValues =
    [
        "Install Dir",
        "InstallPath",
        "Install Path",
        "Folder",
        "Path"
    ];

    // ===== Configuration Files =====

    /// <summary>Configuration files used by game installations.</summary>
    public static readonly string[] ConfigFiles =
    [
        "options.ini",     // Legacy game options
        "skirmish.ini",    // Skirmish settings
        "network.ini",     // Network configuration
    ];

    /// <summary>
    /// The GeneralsOnline executable names that are supported launch entry points.
    /// Since 060526_QFE1 the Easy Anti-Cheat bootstrapper starts the binary named by
    /// <c>EasyAntiCheat/Settings.json</c>; older packages launch the 60Hz binary directly.
    /// <c>GeneralsOnlineZH.exe</c> ships alongside both but is not wrapped, so it is workspace
    /// content rather than an entry point. Unix packages ship the extensionless native
    /// client instead of any Windows launcher.
    /// </summary>
    /// <remarks>
    /// Membership only. When several are present the bootstrapper wins, but that precedence is
    /// expressed in the resolving code rather than by the order of this list.
    /// </remarks>
    public static readonly IReadOnlyList<string> GeneralsOnlineExecutableNames =
    [
        GeneralsOnlineEacLauncherExecutable,
        GeneralsOnline60HzExecutable,
        GeneralsOnlineUnixExecutable,
    ];

    /// <summary>
    /// List of SuperHackers executable names to detect.
    /// SuperHackers releases weekly game client builds for Generals and Zero Hour.
    /// </summary>
    public static readonly IReadOnlyList<string> SuperHackersExecutableNames =
    [
        SuperHackersGeneralsExecutable,  // generalsv.exe
        SuperHackersZeroHourExecutable,  // generalszh.exe
    ];

    /// <summary>
    /// Case-insensitive filename markers identifying base Generals archives that must never be
    /// linked into a Zero Hour workspace: each names content that overrides Zero Hour's own
    /// definitions, crashes the engine, or conflicts with localized string tables.
    /// </summary>
    /// <remarks>
    /// <c>ini</c> matches balance and game-definition archives (INI.big, PatchINI.big);
    /// <c>patch</c> matches patch overrides (Patch.big, PatchData.big, PatchWindow.big);
    /// <c>window</c> matches UI window layouts (Window.big); <c>shader</c> matches legacy
    /// DirectX 8 shaders (shaders.big); <c>gensec</c> matches SafeDisc copy protection
    /// (gensec.big), which triggers a base Generals CD-ROM check;
    /// <c>csf</c>, <c>lang</c>, and <c>string</c> match localization or string table overrides.
    /// </remarks>
    public static readonly IReadOnlyList<string> UnsafeSupplementalArchiveMarkers =
    [
        "ini",
        "patch",
        "window",
        "shader",
        "gensec",
        "csf",
        "lang",
        "string",
    ];

    /// <summary>
    /// Archive entry extensions identifying content that must never be linked into a Zero Hour
    /// workspace: string tables, window layouts, and INI definitions override Zero Hour's own
    /// files and crash the engine.
    /// </summary>
    public static readonly IReadOnlyList<string> UnsafeSupplementalArchiveEntryExtensions =
    [
        ".csf",
        ".wnd",
        ".ini",
    ];

    /// <summary>
    /// Exact base filenames (without extension or directory) of base Generals language archives
    /// that contain string tables (generals.csf) conflicting with Zero Hour's localized string tables.
    /// </summary>
    /// <remarks>
    /// Zero Hour ships its own complete language archives (e.g. EnglishZH.big, GermanZH.big)
    /// containing all base game strings plus Zero Hour additions. Linking base language archives
    /// (e.g. English.big) causes SAGE to mount the base generals.csf first alphabetically, wiping out
    /// Zero Hour specific GUI strings (GUI:StartingMoney, GUI:LimitSuperweapons, GUI:StartingMoneyFormat, etc.).
    /// Audio and speech variants (e.g. AudioEnglish.big, SpeechEnglish.big) are sound-only and safe to link.
    /// </remarks>
    public static readonly IReadOnlyList<string> UnsafeSupplementalLanguageArchiveNames =
    [
        "english",
        "german",
        "french",
        "spanish",
        "italian",
        "korean",
        "polish",
        "chinese",
        "chinesetraditional",
        "portuguesebrazil",
        "brazilian",
        "russian",
    ];

    /// <summary>
    /// List of valid game executable filenames for Generals installations.
    /// </summary>
    /// <remarks>
    /// Probe order matters: <c>game.dat</c> first so version detection resolves the engine
    /// instead of a launcher stub that shares the directory.
    /// </remarks>
    public static readonly IReadOnlyList<string> ValidGeneralsExecutableNames =
    [
        SteamGameDatExecutable,
        GeneralsExecutable,
        SuperHackersGeneralsExecutable,
        GameExecutable,
    ];

    /// <summary>
    /// List of valid game executable filenames for Zero Hour installations.
    /// </summary>
    /// <remarks>
    /// <see cref="ZeroHourExecutable"/> is deliberately absent: it shares its
    /// filename with <see cref="GeneralsExecutable"/>, which already covers it.
    /// Probe order matters: <c>game.dat</c> first so version detection resolves the engine
    /// instead of a launcher stub that shares the directory.
    /// </remarks>
    public static readonly IReadOnlyList<string> ValidZeroHourExecutableNames =
    [
        SteamGameDatExecutable,
        GeneralsExecutable,
        SuperHackersZeroHourExecutable,
        GameExecutable,
        GeneralsOnlineDefaultExecutable,
        GeneralsOnline60HzExecutable,
        GeneralsOnlineEacLauncherExecutable,
        ContraExecutable,
        GeneralsOnlineUnixExecutable,
    ];

    /// <summary>
    /// List of valid game executable filenames for installation verification across all editions.
    /// </summary>
    /// <remarks>
    /// <see cref="ZeroHourExecutable"/> is deliberately absent: it shares its
    /// filename with <see cref="GeneralsExecutable"/>, which already covers it.
    /// Probe order matters: <c>game.dat</c> first so version detection resolves the engine
    /// instead of a launcher stub that shares the directory.
    /// </remarks>
    public static readonly IReadOnlyList<string> ValidGameExecutableNames =
    [
        SteamGameDatExecutable,
        GeneralsExecutable,
        SuperHackersZeroHourExecutable,
        SuperHackersGeneralsExecutable,
        GameExecutable,
        GeneralsOnlineDefaultExecutable,
        GeneralsOnline60HzExecutable,
        GeneralsOnlineEacLauncherExecutable,
        ContraExecutable,
        GeneralsOnlineUnixExecutable,
    ];

    /// <summary>
    /// Action types used in the Setup Wizard.
    /// </summary>
    public static class WizardActionTypes
    {
        /// <summary>Update an existing component.</summary>
        public const string Update = "Update";

        /// <summary>Install a new component.</summary>
        public const string Install = "Install";

        /// <summary>Create a profile for an existing installation.</summary>
        public const string CreateProfile = "CreateProfile";

        /// <summary>Decline the component.</summary>
        public const string Decline = "Decline";

        /// <summary>No action taken.</summary>
        public const string None = "None";
    }

    /// <summary>
    /// Status strings displayed in the Setup Wizard.
    /// </summary>
    public static class WizardStatuses
    {
        /// <summary>Installed status.</summary>
        public const string Installed = "Installed";

        /// <summary>Downloaded status.</summary>
        public const string Downloaded = "Downloaded";

        /// <summary>Detected status.</summary>
        public const string Detected = "Detected";

        /// <summary>Missing status.</summary>
        public const string Missing = "Missing";
    }

    /// <summary>
    /// Action button and toggle labels displayed in the Setup Wizard.
    /// </summary>
    public static class WizardActionLabels
    {
        /// <summary>Update or reinstall action label.</summary>
        public const string UpdateReinstall = "Update / Reinstall";

        /// <summary>Create profile action label.</summary>
        public const string CreateProfile = "Create Profile";

        /// <summary>Download and install action label.</summary>
        public const string DownloadAndInstall = "Download & Install";
    }

    /// <summary>
    /// Deterministic IDs for synthetic game clients used during initial setup.
    /// </summary>
    public static class SyntheticClientIds
    {
        /// <summary>Synthetic ID for Community Patch.</summary>
        public const string CommunityPatch = "cp.synth";

        /// <summary>Synthetic ID for Community Patch (Non-Retail).</summary>
        public const string CommunityPatchNonRet = "community-patch-nonret.synth";

        /// <summary>Synthetic ID for Generals Online.</summary>
        public const string GeneralsOnline = "go.synth";

        /// <summary>Synthetic ID for Super Hackers.</summary>
        public const string SuperHackers = "sh.synth";
    }
}
