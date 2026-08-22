using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services;

/// <summary>
/// An <see cref="HttpContent"/> wrapper around a <see cref="Stream"/> that reports byte upload progress.
/// </summary>
public sealed class ProgressableStreamContent : HttpContent
{
    private const int DefaultBufferSize = 64 * 1024;
    private readonly Stream _content;
    private readonly long _totalBytes;
    private readonly IProgress<double>? _progress;
    private readonly int _bufferSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressableStreamContent"/> class.
    /// </summary>
    /// <param name="content">Underlying readable stream.</param>
    /// <param name="totalBytes">Total bytes expected to be read.</param>
    /// <param name="progress">Progress callback.</param>
    /// <param name="bufferSize">Chunk buffer size in bytes.</param>
    public ProgressableStreamContent(
        Stream content,
        long totalBytes,
        IProgress<double>? progress = null,
        int bufferSize = DefaultBufferSize)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _totalBytes = totalBytes;
        _progress = progress;
        _bufferSize = bufferSize;
    }

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        return SerializeToStreamAsync(stream, context, CancellationToken.None);
    }

    /// <inheritdoc />
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var buffer = new byte[_bufferSize];
        long uploadedBytes = 0;

        if (_content.CanSeek)
        {
            _content.Seek(0, SeekOrigin.Begin);
        }

        while (true)
        {
            var bytesRead = await _content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await stream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            uploadedBytes += bytesRead;

            if (_totalBytes > 0 && _progress != null)
            {
                var fraction = (double)uploadedBytes / _totalBytes;
                _progress.Report(Math.Min(0.99, Math.Max(0.01, fraction)));
            }
        }
    }

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        length = _totalBytes;
        return true;
    }
}
