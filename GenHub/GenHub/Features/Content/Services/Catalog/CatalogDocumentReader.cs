using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Reads a publisher catalog from either an HTTP(S) endpoint or a local file selected by the user.
/// </summary>
/// <remarks>
/// <para>
/// Local file support is intentional for Publisher Studio previews and offline catalog authoring.
/// It is limited to explicit <c>file://</c> URIs or fully qualified file paths; other URI schemes
/// are rejected rather than being passed to <see cref="HttpClient"/>.
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
    /// <param name="httpClient">HTTP client used for HTTP(S) catalog locations.</param>
    /// <param name="catalogLocation">An HTTP(S) URL, a file URI, or a fully qualified file path.</param>
    /// <param name="maximumSizeBytes">Optional maximum permitted catalog size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The catalog JSON payload.</returns>
    /// <exception cref="ArgumentException">Thrown when the location is blank or uses an unsupported scheme.</exception>
    /// <exception cref="InvalidDataException">Thrown when the catalog exceeds the configured size limit.</exception>
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
            EnsureWithinSizeLimit(fileInfo.Exists ? fileInfo.Length : 0, maximumSizeBytes);
            return await File.ReadAllTextAsync(localPath, cancellationToken).ConfigureAwait(false);
        }

        if (!Uri.TryCreate(catalogLocation, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "Catalog locations must use HTTP(S), a file URI, or a fully qualified file path.",
                nameof(catalogLocation));
        }

        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is { } headerLength)
        {
            EnsureWithinSizeLimit(headerLength, maximumSizeBytes);
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

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

    private static string? ResolveLocalPath(string catalogLocation)
    {
        if (Path.IsPathFullyQualified(catalogLocation))
        {
            return catalogLocation;
        }

        return Uri.TryCreate(catalogLocation, UriKind.Absolute, out var uri) && uri.IsFile
            ? uri.LocalPath
            : null;
    }

    private static void EnsureWithinSizeLimit(long contentLength, long? maximumSizeBytes)
    {
        if (maximumSizeBytes is > 0 && contentLength > maximumSizeBytes.Value)
        {
            throw new InvalidDataException($"Catalog exceeds maximum size of {maximumSizeBytes.Value} bytes.");
        }
    }
}
