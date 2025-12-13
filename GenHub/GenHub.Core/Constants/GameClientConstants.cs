namespace GenHub.Core.Constants;

/// <summary>
/// Constants related to game client detection and management.
/// </summary>
public static class GameClientConstants
{
    // ===== Game Executables =====

    /// <summary>Generals executable filename.</summary>
    public const string GeneralsExecutable = "generals.exe";

    /// <summary>Zero Hour executable filename.</summary>
    public const string ZeroHourExecutable = "generals.exe";

    // ===== SuperHackers Client Detection =====

    /// <summary>SuperHackers Generals executable filename.</summary>
    public const string SuperHackersGeneralsExecutable = "generalsv.exe";

    /// <summary>Super Hackers Zero Hour executable filename.</summary>
    public const string SuperHackersZeroHourExecutable = "generalszh.exe";

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

    // ===== GeneralsOnline Client Detection =====

    /// <summary>GeneralsOnline 30Hz client executable name.</summary>
    public const string GeneralsOnline30HzExecutable = "generalsonlinezh_30.exe";

    /// <summary>GeneralsOnline 60Hz client executable name.</summary>
    public const string GeneralsOnline60HzExecutable = "generalsonlinezh_60.exe";

    /// <summary>GeneralsOnline default client executable name.</summary>
    public const string GeneralsOnlineDefaultExecutable = "generalsonlinezh.exe";

    /// <summary>Display name for GeneralsOnline 30Hz variant.</summary>
    public const string GeneralsOnline30HzDisplayName = "GeneralsOnline 30Hz";

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
    public const string AutoDetectedVersion = "Automatically added";

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

    // ===== Required DLLs =====

    /// <summary>
    /// DLLs required for standard game installations.
    /// </summary>
    public static readonly string[] RequiredDlls = new[]
    {
        "steam_api.dll",      // Steam integration
        "binkw32.dll",        // Bink video codec
        "mss32.dll",          // Miles Sound System
        "eauninstall.dll",    // EA App integration
    };

    /// <summary>
    /// DLLs specific to GeneralsOnline installations.
    /// </summary>
    public static readonly string[] GeneralsOnlineDlls = new[]
    {
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
    };

    // ===== Configuration Files =====

    /// <summary>
    /// Configuration files used by game installations.
    /// </summary>
    public static readonly string[] ConfigFiles = new[]
    {
        "options.ini",     // Legacy game options
        "skirmish.ini",    // Skirmish settings
        "network.ini",     // Network configuration
    };

    /// <summary>
    /// List of GeneralsOnline executable names to detect.
    /// Only includes 30Hz and 60Hz variants as these are the primary clients.
    /// GeneralsOnline provides auto-updated clients for Command &amp; Conquer Generals and Zero Hour.
    /// </summary>
    public static readonly IReadOnlyList<string> GeneralsOnlineExecutableNames = new[]
    {
        GeneralsOnline30HzExecutable,
        GeneralsOnline60HzExecutable,
    };
}