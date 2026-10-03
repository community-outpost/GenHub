namespace GenHub.Core.Constants;

/// <summary>
/// Hostname scopes blocked from remote fetching by SSRF guards.
/// </summary>
public static class NetworkSecurityConstants
{
    /// <summary>
    /// The loopback hostname blocked from remote fetching.
    /// </summary>
    public const string BlockedLocalhostName = "localhost";

    /// <summary>
    /// Hostname suffix for loopback-scoped names.
    /// </summary>
    public const string BlockedLocalhostSuffix = ".localhost";

    /// <summary>
    /// Hostname suffix for multicast DNS local-link names.
    /// </summary>
    public const string BlockedLocalSuffix = ".local";

    /// <summary>
    /// Hostname suffix for internal network names.
    /// </summary>
    public const string BlockedInternalSuffix = ".internal";

    /// <summary>
    /// IPv4 loopback literal address.
    /// </summary>
    public const string LoopbackIpv4 = "127.0.0.1";

    /// <summary>
    /// IPv6 loopback literal address.
    /// </summary>
    public const string LoopbackIpv6 = "::1";

    /// <summary>
    /// Error message format when no IP addresses were found for a host.
    /// </summary>
    public const string NoIpAddressesFoundFormat = "No IP addresses found for host '{0}'.";

    /// <summary>
    /// Error message format when a loopback host resolves to a non-loopback IP address.
    /// </summary>
    public const string LoopbackResolvedToNonLoopbackFormat = "Loopback host '{0}' resolved to a non-loopback IP address.";

    /// <summary>
    /// Error message format when a host resolves to an unsafe or reserved IP address.
    /// </summary>
    public const string UnsafeIpAddressFormat = "Host '{0}' resolved to an unsafe or reserved IP address.";
}
