using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
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
    private readonly IDownloadUrlValidator downloadUrlValidator;

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
    /// <param name="urlValidator">Validates each redirect hop; defaults to a new <see cref="DownloadUrlValidator"/>.</param>
    internal SafeMarkdownPathResolver(HttpClient client, IDownloadUrlValidator? urlValidator = null)
    {
        httpClient = client ?? throw new ArgumentNullException(nameof(client));
        downloadUrlValidator = urlValidator ?? new DownloadUrlValidator();
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
        try
        {
            var validated = await SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
                httpClient,
                static uri => new HttpRequestMessage(HttpMethod.Get, uri),
                initialUri,
                ImageCacheConstants.MaxRedirects,
                downloadUrlValidator,
                cancellationToken,
                blockHttpsDowngrade: true).ConfigureAwait(false);
            return validated.Response;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
