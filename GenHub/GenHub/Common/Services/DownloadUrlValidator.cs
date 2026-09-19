using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.Services;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Services;

/// <summary>
/// Validates that download targets are absolute HTTP(S) URIs whose hosts resolve only to
/// public internet addresses. Loopback, private, link-local, and other non-global
/// destinations are rejected, as are hosts that fail to resolve.
/// </summary>
public sealed class DownloadUrlValidator : IDownloadUrlValidator
{
    /// <inheritdoc />
    public async Task<bool> IsSafeAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            return ImageCacheService.IsSafeIpAddress(literal);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken).ConfigureAwait(false);
            return addresses.Length > 0 && addresses.All(ImageCacheService.IsSafeIpAddress);
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
