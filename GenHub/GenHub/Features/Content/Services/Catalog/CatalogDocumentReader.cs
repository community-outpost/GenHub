using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Infrastructure.Services;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Reads a publisher catalog from either an HTTPS endpoint or a local file selected by the user.
/// </summary>
/// <remarks>
/// <para>
/// Local file support is intentional for Publisher Studio previews and offline catalog authoring.
/// It is limited to explicit <c>file://</c> URIs or fully qualified local file paths; UNC paths and
/// other URI schemes are rejected rather than being passed to <see cref="HttpClient"/>.
/// </para>
/// <para>
/// All catalog consumers use this reader so that subscription confirmation, browsing, refreshing,
/// and custom tabs observe the same source semantics.
/// </para>
/// </remarks>
public static class CatalogDocumentReader
{
    /// <summary>
    /// Reads catalog JSON from the supplied catalog location.
    /// </summary>
    /// <param name="httpClient">HTTP client used for HTTPS catalog locations.</param>
    /// <param name="catalogLocation">An HTTPS URL, a local file URI, or a fully qualified local file path.</param>
    /// <param name="maximumSizeBytes">Optional maximum permitted catalog size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalog JSON payload.</returns>
    /// <exception cref="ArgumentException">Thrown when the location is blank, uses an unsupported scheme, or resolves to an unsafe IP address.</exception>
    /// <exception cref="FileNotFoundException">Thrown when a local file does not exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when the catalog exceeds the configured size limit or redirects to an unsafe URI.</exception>
    public static async Task<string> ReadAsync(
        HttpClient httpClient,
        string catalogLocation,
        long? maximumSizeBytes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        if (string.IsNullOrWhiteSpace(catalogLocation))
        {
            throw new ArgumentException("A catalog location is required.", nameof(catalogLocation));
        }

        var localPath = ResolveLocalPath(catalogLocation);
        if (localPath is not null)
        {
            var fileInfo = new FileInfo(localPath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException("Catalog file not found.", localPath);
            }

            EnsureWithinSizeLimit(fileInfo.Length, maximumSizeBytes);

            using var localStream = new FileStream(
                localPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 8192,
                useAsync: true);

            return await ReadStreamWithLimitAsync(localStream, maximumSizeBytes, cancellationToken).ConfigureAwait(false);
        }

        if (!Uri.TryCreate(catalogLocation, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !ImageCacheService.IsSafeRemoteUrl(catalogLocation, out _))
        {
            throw new ArgumentException(
                "Catalog locations must use HTTPS with a safe public host, a local file URI, or a fully qualified local file path.",
                nameof(catalogLocation));
        }

        await ValidateHostDnsSafetyAsync(uri.DnsSafeHost, isRedirect: false, cancellationToken).ConfigureAwait(false);

        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.RequestMessage?.RequestUri != null)
        {
            var redirectUri = response.RequestMessage.RequestUri;
            if (!string.Equals(redirectUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !ImageCacheService.IsSafeRemoteUrl(redirectUri.AbsoluteUri, out _))
            {
                throw new InvalidDataException("Catalog request was redirected to an insecure non-HTTPS or unsafe URI.");
            }

            await ValidateHostDnsSafetyAsync(redirectUri.DnsSafeHost, isRedirect: true, cancellationToken).ConfigureAwait(false);
        }

        if (response.Content.Headers.ContentLength is { } headerLength)
        {
            EnsureWithinSizeLimit(headerLength, maximumSizeBytes);
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ReadStreamWithLimitAsync(stream, maximumSizeBytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidateHostDnsSafetyAsync(string host, bool isRedirect, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var ip))
        {
            if (!ImageCacheService.IsSafeIpAddress(ip))
            {
                throw CreateSafetyException(
                    $"Catalog {(isRedirect ? "redirect target" : "host")} '{host}' resolves to an unsafe IP address.",
                    isRedirect);
            }

            return;
        }

        await ValidateHostDnsAddressesAsync(host, isRedirect, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ValidateHostDnsAddressesAsync(string host, bool isRedirect, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            if (addresses.Length == 0 || addresses.Any(a => !ImageCacheService.IsSafeIpAddress(a)))
            {
                throw CreateSafetyException(
                    $"Catalog {(isRedirect ? "redirect target" : "host")} '{host}' resolves to an unsafe IP address.",
                    isRedirect);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or SocketException)
        {
            if (ex is not SocketException)
            {
                throw;
            }

            // Socket exception indicates unresolved host (e.g. mock test host or offline environment).
            // Allow HttpClient pipeline to handle the request.
        }
        catch (Exception ex)
        {
            throw CreateSafetyException(
                $"Failed to resolve {(isRedirect ? "redirect " : string.Empty)}host '{host}': {ex.Message}",
                isRedirect,
                ex);
        }
    }

    private static Exception CreateSafetyException(string message, bool isRedirect, Exception? innerException = null)
    {
        return isRedirect
            ? new InvalidDataException(message, innerException)
            : new ArgumentException(message, innerException);
    }

    private static string? ResolveLocalPath(string catalogLocation)
    {
        if (Path.IsPathFullyQualified(catalogLocation))
        {
            // Reject UNC paths (\\server\share or //server/share) to prevent SSRF / SMB access
            if (catalogLocation.StartsWith(@"\", StringComparison.Ordinal) ||
                catalogLocation.StartsWith("//", StringComparison.Ordinal))
            {
                return null;
            }

            return catalogLocation;
        }

        if (Uri.TryCreate(catalogLocation, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            // Reject UNC file URIs (file://server/share)
            if (uri.IsUnc || !string.IsNullOrEmpty(uri.Host))
            {
                return null;
            }

            return uri.LocalPath;
        }

        return null;
    }

    private static void EnsureWithinSizeLimit(long contentLength, long? maximumSizeBytes)
    {
        if (maximumSizeBytes is > 0 && contentLength > maximumSizeBytes.Value)
        {
            throw new InvalidDataException($"Catalog exceeds maximum size of {maximumSizeBytes.Value} bytes.");
        }
    }

    private static async Task<string> ReadStreamWithLimitAsync(
        Stream stream,
        long? maximumSizeBytes,
        CancellationToken cancellationToken)
    {
        if (maximumSizeBytes is not > 0)
        {
            using var directReader = new StreamReader(stream, Encoding.UTF8);
            return await directReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        using var memoryStream = new MemoryStream();
        var buffer = new byte[8192];
        long totalBytesRead = 0;
        int bytesRead = 0;

        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalBytesRead += bytesRead;
            if (totalBytesRead > maximumSizeBytes.Value)
            {
                throw new InvalidDataException($"Catalog exceeds maximum size of {maximumSizeBytes.Value} bytes.");
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }

        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
