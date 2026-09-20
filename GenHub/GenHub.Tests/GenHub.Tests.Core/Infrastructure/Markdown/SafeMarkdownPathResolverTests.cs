using GenHub.Infrastructure.Markdown;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Markdown;

/// <summary>
/// Tests for the SSRF-safe markdown image resolver.
/// </summary>
public sealed class SafeMarkdownPathResolverTests
{
    /// <summary>
    /// Verifies that remote images resolve to their exact bytes.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveImageResource_HttpUrl_ReturnsExactBytesAsync()
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("fake-image-bytes");
        using var server = new SingleRequestServer(payload, "image/png");
        var resolver = new SafeMarkdownPathResolver();

        // Act
        using var stream = await server.ServeAndResolveAsync(
            $"http://127.0.0.1:{server.Port}/shot.png",
            url => resolver.ResolveImageResource(url)!);

        // Assert
        var memory = Assert.IsType<MemoryStream>(stream);
        Assert.Equal(payload, memory.ToArray());
    }

    /// <summary>
    /// Verifies that relative image paths are rejected without a network request.
    /// </summary>
    /// <param name="url">The image URL to resolve.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("docs/shot.png")]
    [InlineData("/absolute/path.png")]
    [InlineData("ftp://example.test/shot.png")]
    [InlineData(null)]
    [InlineData("")]
    public async Task ResolveImageResource_NonHttpUrl_ReturnsNullAsync(string? url)
    {
        // Arrange
        var resolver = new SafeMarkdownPathResolver();

        // Act
        var stream = await resolver.ResolveImageResource(url!)!;

        // Assert
        Assert.Null(stream);
    }

    /// <summary>
    /// Verifies that oversized images are rejected from headers without downloading the body.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveImageResource_OversizedContentLength_ReturnsNullAsync()
    {
        // Arrange
        using var server = new SingleRequestServer([], "image/png", declaredLength: 20 * 1024 * 1024);
        var resolver = new SafeMarkdownPathResolver();

        // Act
        using var stream = await server.ServeAndResolveAsync(
            $"http://127.0.0.1:{server.Port}/huge.png",
            url => resolver.ResolveImageResource(url)!);

        // Assert
        Assert.Null(stream);
    }

    private sealed class SingleRequestServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly byte[] _payload;
        private readonly string _contentType;
        private readonly long? _declaredLength;
        private readonly CancellationTokenSource _lifetime = new();

        public SingleRequestServer(byte[] payload, string contentType, long? declaredLength = null)
        {
            _payload = payload;
            _contentType = contentType;
            _declaredLength = declaredLength;
            Port = GetFreePort();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
        }

        public int Port { get; }

        public void Dispose()
        {
            _lifetime.Cancel();
            _listener.Stop();
            _listener.Close();
            _lifetime.Dispose();
        }

        public async Task<Stream?> ServeAndResolveAsync(string url, Func<string, Task<Stream?>> resolve)
        {
            var serveTask = ServeOneAsync();
            var stream = await resolve(url);
            await serveTask;
            return stream;
        }

        private static int GetFreePort()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task ServeOneAsync()
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(_lifetime.Token);
                context.Response.ContentType = _contentType;
                context.Response.ContentLength64 = _declaredLength ?? _payload.Length;
                if (_declaredLength != null)
                {
                    await context.Response.OutputStream.WriteAsync(new byte[1024], _lifetime.Token);
                    context.Response.Abort();
                    return;
                }

                await context.Response.OutputStream.WriteAsync(_payload, _lifetime.Token);
                context.Response.Close();
            }
            catch (OperationCanceledException)
            {
                // Test finished.
            }
            catch (HttpListenerException)
            {
                // Listener stopped.
            }
            catch (ObjectDisposedException)
            {
                // Listener stopped.
            }
        }
    }
}
