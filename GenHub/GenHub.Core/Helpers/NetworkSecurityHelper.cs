namespace GenHub.Core.Helpers;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides network and IP validation helpers to prevent SSRF (Server-Side Request Forgery) attacks.
/// </summary>
public static class NetworkSecurityHelper
{
    /// <summary>
    /// Validates whether a URL is a safe external HTTP or HTTPS URL (not loopback, private, or local network).
    /// </summary>
    /// <param name="url">The URL string to validate.</param>
    /// <param name="failureReason">The error message if validation fails.</param>
    /// <returns><c>true</c> if safe; otherwise, <c>false</c>.</returns>
    public static bool IsSafeUrl(string? url, out string? failureReason)
    {
        if (!TryGetCandidateUri(url, out var uri, out failureReason))
        {
            return false;
        }

        if (IsBlockedHostName(uri))
        {
            failureReason = "Loopback and local addresses are not allowed.";
            return false;
        }

        if (TryGetLiteralAddress(uri, out var literal))
        {
            if (!IsSafeIpAddress(literal))
            {
                failureReason = "Loopback, private, and local addresses are not allowed.";
                return false;
            }

            return true;
        }

        try
        {
            var addresses = Dns.GetHostAddresses(uri.DnsSafeHost);
            if (addresses.Length > 0 && !addresses.All(IsSafeIpAddress))
            {
                failureReason = "Loopback, private, and local addresses are not allowed.";
                return false;
            }
        }
        catch (SocketException)
        {
            // In offline or mocked environments, connection-time SocketsHttpHandler enforces SSRF safety.
        }

        return true;
    }

    /// <summary>
    /// Asynchronously validates whether a URL is a safe external HTTP or HTTPS URL.
    /// Unlike <see cref="IsSafeUrl"/>, host name resolution does not block the calling thread.
    /// </summary>
    /// <param name="url">The URL string to validate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A tuple indicating whether the URL is safe and the error message when it is not.</returns>
    public static async Task<(bool IsSafe, string? FailureReason)> IsSafeUrlAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCandidateUri(url, out var uri, out var failureReason))
        {
            return (false, failureReason);
        }

        if (IsBlockedHostName(uri))
        {
            return (false, "Loopback and local addresses are not allowed.");
        }

        if (TryGetLiteralAddress(uri, out var literal))
        {
            return IsSafeIpAddress(literal)
                ? (true, null)
                : (false, "Loopback, private, and local addresses are not allowed.");
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            if (addresses.Length > 0 && !addresses.All(IsSafeIpAddress))
            {
                return (false, "Loopback, private, and local addresses are not allowed.");
            }
        }
        catch (SocketException)
        {
            // In offline or mocked environments, connection-time SocketsHttpHandler enforces SSRF safety.
        }

        return (true, null);
    }

    /// <summary>
    /// Determines whether an IP address is considered safe from SSRF attack vectors.
    /// Blocks loopback, link-local, private, reserved, multicast, and IPv6 transition embeddings (IPv4-mapped IPv6, 6to4, NAT64, Teredo) of unsafe IPs.
    /// </summary>
    /// <param name="address">The IP address to validate.</param>
    /// <returns><c>true</c> if the IP address is safe; otherwise, <c>false</c>.</returns>
    public static bool IsSafeIpAddress(IPAddress? address)
    {
        if (address == null)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length switch
        {
            4 => IsSafeIPv4(bytes),
            16 => IsSafeIPv6(bytes),
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether an IPv4 address (as 4 bytes) is safe.
    /// </summary>
    /// <param name="b">The 4-byte IPv4 address span.</param>
    /// <returns><c>true</c> if the IPv4 address is safe; otherwise, <c>false</c>.</returns>
    public static bool IsSafeIPv4(ReadOnlySpan<byte> b)
    {
        if (b.Length != 4)
        {
            return false;
        }

        return (b[0], b[1], b[2]) switch
        {
            (0 or 10 or 127, _, _) => false,                  // 0.0.0.0/8 (current network), 10.0.0.0/8 (private), 127.0.0.0/8 (loopback)
            (>= 224, _, _) => false,                          // 224.0.0.0/4 (multicast 224-239), 240.0.0.0/4 (reserved)
            (100, >= 64 and <= 127, _) => false,             // 100.64.0.0/10 (carrier-grade NAT)
            (169, 254, _) => false,                           // 169.254.0.0/16 (link-local / cloud metadata)
            (172, >= 16 and <= 31, _) => false,               // 172.16.0.0/12 (private)
            (192, 0, 0 or 2) => false,                        // 192.0.0.0/24 (IETF), 192.0.2.0/24 (TEST-NET-1)
            (192, 168, _) => false,                           // 192.168.0.0/16 (private)
            (198, 18 or 19, _) => false,                      // 198.18.0.0/15 (benchmark)
            (198, 51, 100) => false,                          // 198.51.100.0/24 (TEST-NET-2)
            (203, 0, 113) => false,                           // 203.0.113.0/24 (TEST-NET-3)
            _ => true,
        };
    }

    /// <summary>
    /// Determines whether an IPv6 address (as 16 bytes) is safe.
    /// </summary>
    /// <param name="b">The 16-byte IPv6 address span.</param>
    /// <returns><c>true</c> if the IPv6 address is safe; otherwise, <c>false</c>.</returns>
    public static bool IsSafeIPv6(ReadOnlySpan<byte> b)
    {
        if (b.Length != 16)
        {
            return false;
        }

        if (IsLoopbackOrUnspecified(b) || IsPrivateOrLocalIpv6(b))
        {
            return false;
        }

        return IsEmbeddedIpv4Safe(b);
    }

    private static bool TryGetCandidateUri(string? url, [NotNullWhen(true)] out Uri? uri, out string? failureReason)
    {
        uri = null;
        failureReason = null;
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            uri = null;
            failureReason = "URL must be a valid absolute HTTP or HTTPS URL.";
            return false;
        }

        return true;
    }

    private static bool IsBlockedHostName(Uri uri) =>
        uri.IsLoopback ||
        uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetLiteralAddress(Uri uri, [NotNullWhen(true)] out IPAddress? address)
    {
        if (IPAddress.TryParse(uri.DnsSafeHost, out address) || IPAddress.TryParse(uri.Host, out address))
        {
            return address != null;
        }

        address = null;
        return false;
    }

    private static bool IsLoopbackOrUnspecified(ReadOnlySpan<byte> b)
    {
        var allZeroExceptLast = true;
        for (var i = 0; i < 15; i++)
        {
            if (b[i] != 0)
            {
                allZeroExceptLast = false;
                break;
            }
        }

        return allZeroExceptLast && (b[15] == 0 || b[15] == 1);
    }

    private static bool IsPrivateOrLocalIpv6(ReadOnlySpan<byte> b)
    {
        // Unique Local Addresses (fc00::/7 -> fc00:: to fdff::)
        if ((b[0] & 0xfe) == 0xfc)
        {
            return true;
        }

        // Link-Local Unicast (fe80::/10 -> fe80:: to febf::)
        if (b[0] == 0xfe && (b[1] & 0xc0) == 0x80)
        {
            return true;
        }

        // Multicast (ff00::/8)
        if (b[0] == 0xff)
        {
            return true;
        }

        // Local-Use NAT64 prefix (64:ff9b:1::/48)
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4] == 0x00 && b[5] == 0x01)
        {
            return true;
        }

        return false;
    }

    private static bool IsEmbeddedIpv4Safe(ReadOnlySpan<byte> b)
    {
        // IPv4-mapped IPv6 (::ffff:0:0/96)
        if (IsIpv4MappedPrefix(b))
        {
            return IsSafeIPv4(b.Slice(12, 4));
        }

        // IPv4-compatible IPv6 (deprecated, ::0:0/96)
        if (IsIpv4CompatiblePrefix(b))
        {
            return IsSafeIPv4(b.Slice(12, 4));
        }

        // 6to4 translation (2002::/16)
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsSafeIPv4(b.Slice(2, 4));
        }

        // Teredo tunneling (2001:0000::/32)
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)
        {
            Span<byte> xoredIpv4 = stackalloc byte[4];
            xoredIpv4[0] = (byte)(b[12] ^ 0xff);
            xoredIpv4[1] = (byte)(b[13] ^ 0xff);
            xoredIpv4[2] = (byte)(b[14] ^ 0xff);
            xoredIpv4[3] = (byte)(b[15] ^ 0xff);
            return IsSafeIPv4(xoredIpv4);
        }

        return IsNat64WellKnownSafe(b);
    }

    private static bool IsIpv4MappedPrefix(ReadOnlySpan<byte> b)
    {
        for (var i = 0; i < 10; i++)
        {
            if (b[i] != 0)
            {
                return false;
            }
        }

        return b[10] == 0xff && b[11] == 0xff;
    }

    private static bool IsIpv4CompatiblePrefix(ReadOnlySpan<byte> b)
    {
        for (var i = 0; i < 12; i++)
        {
            if (b[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNat64WellKnownSafe(ReadOnlySpan<byte> b)
    {
        if (b[0] != 0x00 || b[1] != 0x64 || b[2] != 0xff || b[3] != 0x9b)
        {
            return true;
        }

        for (var i = 4; i < 12; i++)
        {
            if (b[i] != 0)
            {
                return true;
            }
        }

        return IsSafeIPv4(b.Slice(12, 4));
    }
}
