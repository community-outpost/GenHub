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
}
