using System;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for the Online (virtual LAN) feature.
/// </summary>
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
    /// Error code for a lost presence channel.
    /// </summary>
    public const string ErrorPresenceLost = "online.presence-lost";

    /// <summary>
    /// Error code for a missing game profile on play.
    /// </summary>
    public const string ErrorProfileMissing = "online.profile-missing";

    /// <summary>
    /// Error code for a failed game launch from the Online tab.
    /// </summary>
    public const string ErrorLaunchFailed = "online.launch-failed";

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
    /// Overlay name reported by the edge until the Phase 0 selection lands.
    /// </summary>
    public const string OverlayPendingSelection = "pending-selection";

    /// <summary>
    /// UDP ports used by Generals and Zero Hour LAN discovery and gameplay.
    /// </summary>
    public static readonly int[] LanUdpPorts = [4321, 27900];

    /// <summary>
    /// TCP ports used by legacy Generals online services.
    /// </summary>
    public static readonly int[] LegacyOnlineTcpPorts = [6667, 28910, 29900, 29920];

    /// <summary>
    /// Gets a value indicating whether the Online feature is enabled.
    /// </summary>
    public static bool IsOnlineEnabled =>
        Environment.GetEnvironmentVariable(EnabledEnvVar) is not "0" and not "false" and not "False" and not "FALSE";
}
