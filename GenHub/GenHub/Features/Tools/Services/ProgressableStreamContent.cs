using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// An <see cref="HttpContent"/> wrapper around a <see cref="Stream"/> that reports byte upload progress.
/// </summary>
public sealed class ProgressableStreamContent(
    Stream content,
    long totalBytes,
    IProgress<double>? progress = null,
    int bufferSize = ToolConstants.DefaultUploadBufferSize) : HttpContent
{
    private const double MinProgressFraction = 0.01;
    private const double MaxProgressFraction = 0.99;

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    /// <inheritdoc />
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize);

        var buffer = new byte[bufferSize];
        long uploadedBytes = 0;

        if (content.CanSeek)
        {
            content.Seek(0, SeekOrigin.Begin);
        }

        while (true)
        {
            var bytesRead = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            uploadedBytes += bytesRead;

            if (totalBytes > 0 && progress != null)
            {
                var fraction = (double)uploadedBytes / totalBytes;
                progress.Report(Math.Min(MaxProgressFraction, Math.Max(MinProgressFraction, fraction)));
            }
        }
    }

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        length = totalBytes;
        return true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            content.Dispose();
        }

        base.Dispose(disposing);
    }
}
