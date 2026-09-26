using GenHub.Core.Constants;
using GenHub.Infrastructure.Services;
using Markdown.Avalonia.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Infrastructure.Markdown;

/// <summary>
/// A restricted path resolver for Markdown viewers rendering untrusted content.
/// Restricts image loading strictly to safe remote HTTP and HTTPS URLs, blocking loopback,
/// private, and link-local destinations as well as unsafe redirects.
/// </summary>
public sealed class SafeMarkdownPathResolver : IPathResolver
{
    private const int MaxImageSizeBytes = 10 * 1024 * 1024; // 10 MB cap
    private const int RequestTimeoutSeconds = 10;

    private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

    private readonly HttpClient httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeMarkdownPathResolver"/> class
    /// using the shared SSRF-safe HTTP client.
    /// </summary>
    public SafeMarkdownPathResolver()
        : this(SharedHttpClient)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeMarkdownPathResolver"/> class with an explicit HTTP client.
    /// Internal constructor for test isolation.
    /// </summary>
    /// <param name="client">The <see cref="HttpClient"/> used to fetch remote images.</param>
    internal SafeMarkdownPathResolver(HttpClient client)
    {
        httpClient = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc/>
    public string? AssetPathRoot { get; set; }

    /// <inheritdoc/>
    public IEnumerable<string>? CallerAssemblyNames { get; set; }

    /// <inheritdoc/>
    public async Task<Stream?>? ResolveImageResource(string relativeOrAbsolutePath)
    {
        // Only permit safe remote http and https destinations for images in untrusted content.
        // Reject local files (file:), application assets (avares:), UNC, relative paths,
        // and loopback, private, or link-local network destinations.
        if (!ImageCacheService.IsSafeRemoteUrl(relativeOrAbsolutePath, out var initialUri))
        {
            return null;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds));
            using var response = await SendWithRedirectsAsync(initialUri, cts.Token).ConfigureAwait(false);
            if (response == null || !response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageSizeBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            return await ReadCappedStreamAsync(stream, cts.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static HttpClient CreateSharedHttpClient()
    {
        // Per-hop redirect validation in SendWithRedirectsAsync requires a handler that
        // does not follow redirects itself. The SSRF-safe handler disables automatic
        // redirection and blocks unsafe IPs at the socket level as a second layer.
        var handler = ImageCacheService.CreateSsrfSafeSocketsHttpHandler();
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds),
        };
    }

    private static bool TryGetRedirectTarget(
        HttpResponseMessage response,
        Uri currentUri,
        out Uri? nextUri,
        out bool isBlocked)
    {
        nextUri = null;
        isBlocked = false;

        if ((int)response.StatusCode is not (>= 300 and <= 399) || response.Headers.Location == null)
        {
            return false;
        }

        var location = response.Headers.Location;
        try
        {
            nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
        }
        catch (UriFormatException)
        {
            isBlocked = true;
            return false;
        }

        if (currentUri.Scheme == Uri.UriSchemeHttps && nextUri.Scheme == Uri.UriSchemeHttp)
        {
            nextUri = null;
            isBlocked = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Detects a redirect that the HTTP handler followed transparently, bypassing the
    /// per-hop validation because no 3xx was ever surfaced. The final destination is
    /// exposed via <see cref="HttpResponseMessage.RequestMessage"/>.
    /// </summary>
    /// <param name="response">The received response.</param>
    /// <param name="requestUri">The URI that was requested.</param>
    /// <returns><c>true</c> when the handler landed on an unsafe or downgraded URI.</returns>
    private static bool IsTransparentRedirectToUnsafeTarget(HttpResponseMessage response, Uri requestUri)
    {
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri == null || finalUri.AbsoluteUri == requestUri.AbsoluteUri)
        {
            return false;
        }

        if (!ImageCacheService.IsSafeRemoteUrl(finalUri.AbsoluteUri, out _))
        {
            return true;
        }

        return requestUri.Scheme == Uri.UriSchemeHttps && finalUri.Scheme == Uri.UriSchemeHttp;
    }

    private static async Task<MemoryStream?> ReadCappedStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        var memoryStream = new MemoryStream();
        try
        {
            var buffer = new byte[81920];
            int bytesRead = 0;
            long totalBytes = 0;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > MaxImageSizeBytes)
                {
                    await memoryStream.DisposeAsync().ConfigureAwait(false);
                    return null;
                }

                await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }

            memoryStream.Position = 0;
            return memoryStream;
        }
        catch
        {
            await memoryStream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<HttpResponseMessage?> SendWithRedirectsAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        var currentUri = initialUri;
        for (var hop = 0; hop <= ImageCacheConstants.MaxRedirects; hop++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ImageCacheService.IsSafeRemoteUrl(currentUri.AbsoluteUri, out var safeUri))
            {
                return null;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, safeUri);
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!TryGetRedirectTarget(response, safeUri, out var nextUri, out var isBlocked))
            {
                if (IsTransparentRedirectToUnsafeTarget(response, safeUri))
                {
                    response.Dispose();
                    return null;
                }

                return response;
            }

            response.Dispose();
            if (isBlocked || nextUri == null)
            {
                return null;
            }

            currentUri = nextUri;
        }

        return null;
    }
}
