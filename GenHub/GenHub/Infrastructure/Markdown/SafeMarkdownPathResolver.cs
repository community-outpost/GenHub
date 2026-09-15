using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Markdown.Avalonia.Utils;

namespace GenHub.Infrastructure.Markdown;

/// <summary>
/// A restricted path resolver for Markdown viewers rendering untrusted content.
/// Restricts image loading strictly to HTTP and HTTPS URLs, preventing unauthorized local file or asset access.
/// </summary>
public sealed class SafeMarkdownPathResolver : IPathResolver
{
    private const int MaxImageSizeBytes = 10 * 1024 * 1024; // 10 MB cap

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    /// <inheritdoc/>
    public string? AssetPathRoot { get; set; }

    /// <inheritdoc/>
    public IEnumerable<string>? CallerAssemblyNames { get; set; }

    /// <inheritdoc/>
    public async Task<Stream?>? ResolveImageResource(string relativeOrAbsolutePath)
    {
        // Only permit http and https schemes for images in untrusted content.
        // Reject local files (file:), application assets (avares:), UNC, and relative paths.
        if (!Uri.TryCreate(relativeOrAbsolutePath, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageSizeBytes)
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
}
