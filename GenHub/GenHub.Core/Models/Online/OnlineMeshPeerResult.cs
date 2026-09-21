namespace GenHub.Core.Models.Online;

/// <summary>
/// Mesh probe outcome for one roster member with a direct endpoint.
/// Relay members publish no endpoint and are never probed.
/// </summary>
public sealed record OnlineMeshPeerResult
{
    /// <summary>
    /// Gets the peer overlay IP address.
    /// </summary>
    public required string OverlayIp { get; init; }

    /// <summary>
    /// Gets a value indicating whether the peer echoed a mesh probe.
    /// </summary>
    public bool Reachable { get; init; }
}
