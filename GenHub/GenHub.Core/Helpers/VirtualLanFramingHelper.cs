using GenHub.Core.Constants;
using System;
using System.Net;
using System.Text;

namespace GenHub.Core.Helpers;

/// <summary>
/// Shared framing and packet-manipulation helpers for virtual LAN tunneling components.
/// </summary>
public static class VirtualLanFramingHelper
{
    /// <summary>
    /// Parses a network ID string into a canonical 16-byte representation.
    /// Prefers Guid byte array representation when valid, falling back to UTF-8.
    /// </summary>
    /// <param name="networkId">The network identifier string.</param>
    /// <returns>A 16-byte array identifying the network.</returns>
    public static byte[] ParseNetworkIdBytes(string networkId)
    {
        var netBytes = new byte[16];
        if (Guid.TryParse(networkId, out var netGuid))
        {
            var guidBytes = netGuid.ToByteArray();
            Buffer.BlockCopy(guidBytes, 0, netBytes, 0, 16);
        }
        else
        {
            var rawStrBytes = Encoding.UTF8.GetBytes(networkId);
            var copyLen = Math.Min(rawStrBytes.Length, 16);
            Buffer.BlockCopy(rawStrBytes, 0, netBytes, 0, copyLen);
        }

        return netBytes;
    }

    /// <summary>
    /// Calculates the subnet-directed broadcast IPv4 address for a given IP and prefix length.
    /// Defaults to <see cref="OnlineConstants.TunOverlayPrefixLength"/> if prefix length is out of range.
    /// </summary>
    /// <param name="ip">The host IP address.</param>
    /// <param name="prefixLength">The network prefix length (CIDR).</param>
    /// <returns>The 4-byte directed broadcast address.</returns>
    public static byte[] CalculateDirectedBroadcast(IPAddress ip, int prefixLength)
    {
        var ipBytes = ip.GetAddressBytes();
        if (ipBytes.Length != 4)
        {
            return [255, 255, 255, 255];
        }

        if (prefixLength is < 0 or > 32)
        {
            prefixLength = OnlineConstants.TunOverlayPrefixLength;
        }

        uint ipNum = ((uint)ipBytes[0] << 24) | ((uint)ipBytes[1] << 16) | ((uint)ipBytes[2] << 8) | ipBytes[3];
        uint hostMask = prefixLength == 32 ? 0 : uint.MaxValue >> prefixLength;
        uint broadcastNum = ipNum | hostMask;

        return
        [
            (byte)((broadcastNum >> 24) & 0xFF),
            (byte)((broadcastNum >> 16) & 0xFF),
            (byte)((broadcastNum >> 8) & 0xFF),
            (byte)(broadcastNum & 0xFF),
        ];
    }
}
