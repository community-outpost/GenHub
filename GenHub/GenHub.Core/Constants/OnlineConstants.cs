using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Online (virtual LAN) feature.
/// </summary>
[SuppressMessage("Security", "S1313:Using hardcoded IP addresses is security-sensitive", Justification = "Default RFC 1918 private overlay fallback IP.")]
public static class OnlineConstants
{
    /// <summary>
    /// Environment variable that toggles the Online tab and its services.
    /// Enabled by default; set to "0" or "false" to disable.
    /// </summary>
    public const string EnabledEnvVar = "GENHUB_ONLINE_ENABLED";

    /// <summary>
    /// Default slot cap for newly created networks.
    /// </summary>
    public const int DefaultSlotCap = 8;

    /// <summary>
    /// Maximum slot cap allowed for a network.
    /// </summary>
    public const int MaxSlotCap = 16;

    /// <summary>
    /// Minimum network name length.
    /// </summary>
    public const int MinNetworkNameLength = 3;

    /// <summary>
    /// Maximum network name length.
    /// </summary>
    public const int MaxNetworkNameLength = 64;

    /// <summary>
    /// Maximum LAN nickname length in characters. Mirrors
    /// <c>g_lanPlayerNameLength</c> in the game engine: longer names are
    /// silently truncated by the LAN lobby.
    /// </summary>
    public const int MaxNicknameLength = 12;

    /// <summary>
    /// Error code for a blank nickname that must leave Network.ini untouched.
    /// </summary>
    public const string ErrorNicknameEmpty = "online.nickname-empty";

    /// <summary>
    /// Minimum network password length.
    /// </summary>
    public const int MinPasswordLength = 4;

    /// <summary>
    /// Maximum network password length.
    /// </summary>
    public const int MaxPasswordLength = 128;

    /// <summary>
    /// HTTP timeout in seconds for directory and session calls.
    /// </summary>
    public const int HttpTimeoutSeconds = 15;

    /// <summary>
    /// Presence heartbeat interval in seconds.
    /// </summary>
    public const int PresenceHeartbeatSeconds = 30;

    /// <summary>
    /// Initial reconnect delay in seconds for presence and adapter recovery.
    /// </summary>
    public const int ReconnectInitialDelaySeconds = 2;

    /// <summary>
    /// Maximum reconnect delay in seconds for presence and adapter recovery.
    /// </summary>
    public const int ReconnectMaxDelaySeconds = 60;

    /// <summary>
    /// Prefix shared by every Online edge error code.
    /// </summary>
    public const string ErrorCodePrefix = "online.";

    /// <summary>
    /// Lead time in seconds before grant expiry to proactively refresh it.
    /// </summary>
    public const int GrantRefreshLeadTimeSeconds = 120;

    /// <summary>
    /// Error code for a wrong network password.
    /// </summary>
    public const string ErrorWrongPassword = "online.wrong-password";

    /// <summary>
    /// Error code for a full network.
    /// </summary>
    public const string ErrorNetworkFull = "online.network-full";

    /// <summary>
    /// Error code for a banned member.
    /// </summary>
    public const string ErrorNetworkBanned = "online.network-banned";

    /// <summary>
    /// Error code for a missing network.
    /// </summary>
    public const string ErrorNetworkNotFound = "online.network-not-found";

    /// <summary>
    /// Error code for adapter bring-up failure.
    /// </summary>
    public const string ErrorAdapterFailed = "online.adapter-failed";

    /// <summary>
    /// Error code for an unreachable edge service.
    /// </summary>
    public const string ErrorServiceUnavailable = "online.service-unavailable";

    /// <summary>
    /// Error code for a missing or expired session token.
    /// </summary>
    public const string ErrorSessionRequired = "online.session-required";

    /// <summary>
    /// Error code for a missing game profile on play.
    /// </summary>
    public const string ErrorProfileMissing = "online.profile-missing";

    /// <summary>
    /// Error code for an invalid network name length.
    /// </summary>
    public const string ErrorInvalidName = "online.invalid-name";

    /// <summary>
    /// Error code for an out-of-range slot cap.
    /// </summary>
    public const string ErrorInvalidSlots = "online.invalid-slots";

    /// <summary>
    /// Error code for an overlong network password.
    /// </summary>
    public const string ErrorPasswordTooLong = "online.password-too-long";

    /// <summary>
    /// Error code for a present-but-short network password.
    /// </summary>
    public const string ErrorPasswordTooShort = "online.password-too-short";

    /// <summary>
    /// Error code from older edge deployments that required a password for
    /// public lobbies. Current edges allow passwordless lobbies.
    /// </summary>
    public const string ErrorPasswordRequired = "online.password-required";

    /// <summary>
    /// Directory entries older than this are corpses whose room died without
    /// removing them. Mirrors the edge STALE_ENTRY_SECONDS filter so mixed
    /// client/edge versions still hide dead lobbies.
    /// </summary>
    public const int DirectoryStaleSeconds = 3600;

    /// <summary>
    /// Error code for a failed game launch from the Online tab.
    /// </summary>
    public const string ErrorLaunchFailed = "online.launch-failed";

    /// <summary>
    /// Error code for a failed game stop from the Online tab.
    /// </summary>
    public const string ErrorStopFailed = "online.stop-failed";

    /// <summary>
    /// Environment variable overriding the overlay sidecar binary path.
    /// </summary>
    public const string OverlayBinaryEnvVar = "GENHUB_OVERLAY_BIN";

    /// <summary>
    /// Grace period in milliseconds for the sidecar to stay alive after spawn.
    /// </summary>
    public const int SidecarStartupGraceMs = 2000;

    /// <summary>
    /// Timeout in milliseconds for sidecar shutdown.
    /// </summary>
    public const int SidecarStopTimeoutMs = 5000;

    /// <summary>
    /// Delay in milliseconds before retrying deletion of a staged sidecar
    /// config that was still locked by the sidecar process.
    /// </summary>
    public const int SidecarConfigDeleteRetryDelayMs = 30000;

    /// <summary>
    /// Overlay name reported by the edge until the Phase 0 selection lands.
    /// </summary>
    public const string OverlayPendingSelection = "pending-selection";

    /// <summary>
    /// Directory name under the system temp path for staged sidecar configs.
    /// </summary>
    public const string SidecarConfigDirectory = "genhub-online";

    /// <summary>
    /// Filename prefix for staged sidecar configuration files.
    /// </summary>
    public const string SidecarConfigPrefix = "overlay-";

    /// <summary>
    /// Product directory name for the installed overlay sidecar.
    /// </summary>
    public const string OverlayInstallDir = "GenHub";

    /// <summary>
    /// Subdirectory name for the installed overlay sidecar.
    /// </summary>
    public const string OverlaySubDir = "overlay";

    /// <summary>
    /// Windows overlay sidecar executable name.
    /// </summary>
    public const string OverlayWindowsBinary = "genhub-overlay.exe";

    /// <summary>
    /// Unix overlay sidecar executable name.
    /// </summary>
    public const string OverlayUnixBinary = "genhub-overlay";

    /// <summary>
    /// Linux TUN control device path.
    /// </summary>
    public const string TunDevicePath = "/dev/net/tun";

    /// <summary>
    /// Sysfs directory listing network interfaces, used to probe TUN existence.
    /// </summary>
    public const string TunSysClassNetDirectory = "/sys/class/net";

    /// <summary>
    /// Default TUN interface name for the overlay.
    /// </summary>
    public const string TunDefaultInterfaceName = "genhub0";

    /// <summary>
    /// Default TUN interface name for the overlay on Linux.
    /// </summary>
    public const string TunDefaultLinuxInterfaceName = "genhub0";

    /// <summary>
    /// Default TUN interface name for the overlay on Windows.
    /// </summary>
    public const string TunDefaultWindowsInterfaceName = "GenHub";

    /// <summary>
    /// Default MTU for the overlay TUN interface.
    /// </summary>
    public const int TunDefaultMtu = 1400;

    /// <summary>
    /// Default overlay subnet prefix length (10.42.0.0/20).
    /// </summary>
    public const int TunOverlayPrefixLength = 20;

    /// <summary>
    /// Default TUN subnet mask (corresponds to /20 prefix length).
    /// </summary>
    public const string DefaultTunSubnetMask = "255.255.240.0";

    /// <summary>
    /// Sidecar exit code for clean shutdown.
    /// </summary>
    public const int SidecarExitSuccess = 0;

    /// <summary>
    /// Sidecar exit code for command line usage errors.
    /// </summary>
    public const int SidecarExitUsage = 1;

    /// <summary>
    /// Sidecar exit code for configuration errors.
    /// </summary>
    public const int SidecarExitConfigError = 2;

    /// <summary>
    /// Sidecar exit code for TUN attach failures.
    /// </summary>
    public const int SidecarExitAttachFailed = 3;

    /// <summary>
    /// Version prefix for online profile fingerprints (length-prefixed hash).
    /// </summary>
    public const string ProfileFingerprintPrefix = "opf3";

    /// <summary>
    /// Fingerprint version carrying engine compatibility CRCs (iniCRC/exeCRC
    /// from <c>IGameCrcCalculatorService</c>) after the content hash.
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
    /// Default virtual LAN IP fallback address.
    /// </summary>
    [SuppressMessage("Security", "S1313:Using hardcoded IP addresses is security-sensitive", Justification = "Default RFC 1918 private overlay fallback IP.")]
    public const string DefaultOverlayFallbackIp = "10.42.0.2"; // NOSONAR

    /// <summary>
    /// Default UDP port for STUN reflexive endpoint discovery.
    /// </summary>
    public const int StunPort = 3478;

    /// <summary>
    /// Timeout in seconds for a single STUN binding transaction.
    /// </summary>
    public const int StunTimeoutSeconds = 5;

    /// <summary>
    /// Hole-punch packets sent per peer connection attempt.
    /// </summary>
    public const int PunchPacketCount = 3;

    /// <summary>
    /// Mesh probe attempts per peer before it counts as unreachable.
    /// </summary>
    public const int MeshCheckAttempts = 2;

    /// <summary>
    /// Timeout in milliseconds waiting for one mesh probe echo.
    /// </summary>
    public const int MeshProbeTimeoutMs = 1500;

    /// <summary>
    /// Random token bytes appended to the punch magic in mesh probes.
    /// </summary>
    public const int MeshProbeTokenBytes = 8;

    /// <summary>
    /// Zero Hour LAN discovery port the tunnel runner listens on.
    /// </summary>
    public const int ZeroHourDiscoveryPort = 8086;

    /// <summary>
    /// Zero Hour game traffic port the tunnel runner delivers to.
    /// </summary>
    public const int ZeroHourGamePort = 16000;

    /// <summary>
    /// Native SO_REUSEPORT option value on Linux for UDP port sharing.
    /// </summary>
    /// <remarks>
    /// The SocketOptionName enumeration exposes no ReusePort member, so the
    /// native value is passed through. Linux and macOS disagree on the value.
    /// </remarks>
    public const int SocketReusePortLinux = 15;

    /// <summary>
    /// Native SO_REUSEPORT option value on macOS for UDP port sharing.
    /// </summary>
    /// <remarks>
    /// The SocketOptionName enumeration exposes no ReusePort member, so the
    /// native value is passed through. Linux and macOS disagree on the value.
    /// </remarks>
    public const int SocketReusePortMacOS = 512;

    /// <summary>
    /// Native SOL_SOCKET level value on Linux for raw socket options.
    /// </summary>
    /// <remarks>
    /// The raw socket option API takes native levels verbatim: unlike the
    /// managed SocketOptionLevel enumeration, Linux expects 1 here.
    /// </remarks>
    public const int SocketLevelLinux = 1;

    /// <summary>
    /// Native SOL_SOCKET level value on macOS for raw socket options.
    /// </summary>
    /// <remarks>
    /// The raw socket option API takes native levels verbatim: unlike the
    /// managed SocketOptionLevel enumeration, Linux expects 1 here.
    /// </remarks>
    public const int SocketLevelMacOS = 0xFFFF;

    /// <summary>
    /// Default UDP port of the relay server.
    /// </summary>
    public const int DefaultRelayPort = 8088;

    /// <summary>
    /// Keep-alive registration interval in seconds for the relay server.
    /// </summary>
    public const int KeepAliveIntervalSeconds = 15;

    /// <summary>
    /// Connection-outcome value for a directly reached peer.
    /// </summary>
    public const string OutcomeDirect = "direct";

    /// <summary>
    /// Connection-outcome value for a relay-only peer.
    /// </summary>
    public const string OutcomeRelay = "relay";

    /// <summary>
    /// Connection-outcome value for an unreachable peer.
    /// </summary>
    public const string OutcomeFailed = "failed";

    /// <summary>
    /// STUN binding request message type.
    /// </summary>
    public const ushort StunBindingRequest = 0x0001;

    /// <summary>
    /// STUN binding success response message type.
    /// </summary>
    public const ushort StunBindingResponse = 0x0101;

    /// <summary>
    /// STUN XOR-MAPPED-ADDRESS attribute type.
    /// </summary>
    public const ushort StunXorMappedAddress = 0x0020;

    /// <summary>
    /// STUN magic cookie (RFC 5389 section 6).
    /// </summary>
    public const uint StunMagicCookie = 0x2112A442;

    /// <summary>
    /// Presence socket close code for an ended membership (network deleted or left).
    /// </summary>
    public const int PresenceCloseMembershipEnded = 4000;

    /// <summary>
    /// Presence socket close code for a banned member.
    /// </summary>
    public const int PresenceCloseBanned = 4001;

    /// <summary>
    /// Plain WebSocket URI scheme for presence channels.
    /// </summary>
    public const string WebSocketScheme = "ws";

    /// <summary>
    /// Secure WebSocket URI scheme for presence channels.
    /// </summary>
    public const string WebSocketSecureScheme = "wss";

    /// <summary>
    /// Presence socket message type carrying the live roster.
    /// </summary>
    public const string PresenceMessageRoster = "roster";

    /// <summary>
    /// Presence socket message type for lobby events. The same token names
    /// the event property inside the envelope.
    /// </summary>
    public const string PresenceMessageEvent = "event";

    /// <summary>
    /// Lobby event name broadcast when the host switches expected profile.
    /// </summary>
    public const string PresenceEventProfileChanged = "profile-changed";

    /// <summary>
    /// Presence socket message type for member heartbeats.
    /// </summary>
    public const string PresenceMessageHeartbeat = "heartbeat";

    /// <summary>
    /// Retired fingerprint prefixes still accepted when extracting the game
    /// client key, so mixed-version lobbies keep same-client detection.
    /// </summary>
    public static readonly IReadOnlyList<string> LegacyProfileFingerprintPrefixes = ["opf1", "opf2"];

    /// <summary>
    /// Gets a value indicating whether the Online feature is enabled.
    /// </summary>
    public static bool IsOnlineEnabled =>
        Environment.GetEnvironmentVariable(EnabledEnvVar)?.Trim().ToLowerInvariant() is not ("0" or "false");

    /// <summary>
    /// Creates a fresh wire-protocol magic ("GHP1") buffer for UDP hole-punch
    /// packets. A new array is returned on every call so senders cannot mutate
    /// shared state.
    /// </summary>
    /// <returns>A new punch magic buffer.</returns>
    public static byte[] GetPunchMagic() => [0x47, 0x48, 0x50, 0x31];

    /// <summary>
    /// Gets the native socket level and option name enabling UDP port sharing (SO_REUSEPORT) on Unix.
    /// </summary>
    /// <remarks>
    /// The SocketOptionName enumeration exposes no ReusePort member, so callers pass
    /// these native values through the raw socket option API. Linux and macOS disagree
    /// on both numbers. Note that SO_REUSEPORT enables port sharing only among cooperative
    /// sockets that all specify the option; uncooperative processes (such as the legacy game engine)
    /// binding without reuse options will still encounter EADDRINUSE if another socket holds the port.
    /// Windows shares UDP ports through SO_REUSEADDR alone and does not use this option.
    /// </remarks>
    /// <returns>The native level and option name for the current Unix platform.</returns>
    public static (int Level, int Name) GetReusePortOption() =>
        OperatingSystem.IsMacOS() ? (SocketLevelMacOS, SocketReusePortMacOS) : (SocketLevelLinux, SocketReusePortLinux);
}
