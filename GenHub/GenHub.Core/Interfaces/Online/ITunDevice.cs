using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Abstraction for a layer-3 TUN virtual network device streaming raw IP packets.
/// </summary>
public interface ITunDevice : IDisposable
{
    /// <summary>
    /// Gets the network interface name on the operating system (e.g., "GenHub" or "genhub0").
    /// </summary>
    string InterfaceName { get; }

    /// <summary>
    /// Gets the assigned overlay IPv4 address.
    /// </summary>
    IPAddress OverlayIp { get; }

    /// <summary>
    /// Reads a single raw IPv4 packet asynchronously from the TUN device.
    /// </summary>
    /// <param name="buffer">The destination buffer to hold the raw IP packet.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of bytes read into the buffer.</returns>
    Task<int> ReadPacketAsync(byte[] buffer, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a single raw IPv4 packet asynchronously to the TUN device.
    /// </summary>
    /// <param name="packet">The raw IP packet to inject into the operating system network stack.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the write operation.</returns>
    ValueTask WritePacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
}
