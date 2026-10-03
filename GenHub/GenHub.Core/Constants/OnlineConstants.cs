using System.Collections.Generic;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Online multiplayer feature.
/// </summary>
public static class OnlineConstants
{
    /// <summary>
    /// Feature flag controlling visibility of the Online tab.
    /// </summary>
    public const bool IsOnlineEnabled = true;

    /// <summary>
    /// Identifier for the Generals Online section in the Online sidebar.
    /// </summary>
    public const string SectionGeneralsOnline = "GeneralsOnline";

    /// <summary>
    /// Width of the open sidebar pane in device-independent units.
    /// </summary>
    public const double SidebarOpenPaneLength = 264.0;

    /// <summary>
    /// Cache time-to-live in minutes for game profile CRC calculation and verification.
    /// </summary>
    public const int ProfileSetupCacheTtlMinutes = 10;

    /// <summary>
    /// Initial delay in seconds before attempting to reconnect a dropped WebSocket session.
    /// </summary>
    public const int ReconnectInitialDelaySeconds = 2;

    /// <summary>
    /// Maximum delay in seconds between WebSocket reconnection attempts.
    /// </summary>
    public const int ReconnectMaxDelaySeconds = 30;

    /// <summary>
    /// Error code when a game profile required for launch cannot be found.
    /// </summary>
    public const string ErrorProfileMissing = "online.launch.profile-missing";

    /// <summary>
    /// Error code when launching a profile fails.
    /// </summary>
    public const string ErrorLaunchFailed = "online.launch.failed";

    /// <summary>
    /// Error code when stopping a running profile fails.
    /// </summary>
    public const string ErrorStopFailed = "online.launch.stop-failed";

    /// <summary>
    /// Version prefix for online profile fingerprints (length-prefixed hash).
    /// </summary>
    public const string ProfileFingerprintPrefix = "opf3";

    /// <summary>
    /// Fingerprint version carrying engine compatibility CRCs (iniCRC/exeCRC) after the content hash.
    /// </summary>
    public const string ProfileFingerprintV4Prefix = "opf4";

    /// <summary>
    /// Maximum expected content ids published per lobby (mirrors the edge).
    /// </summary>
    public const int MaxExpectedContentIds = 32;

    /// <summary>
    /// Separator between fingerprint segments.
    /// </summary>
    public const string FingerprintSeparator = "|";

    /// <summary>
    /// Separator between content ids inside a fingerprint.
    /// </summary>
    public const string FingerprintListSeparator = ",";

    /// <summary>
    /// Retired fingerprint prefixes still accepted when extracting the game client key.
    /// </summary>
    public static readonly IReadOnlyList<string> LegacyProfileFingerprintPrefixes = ["opf1", "opf2"];
}
