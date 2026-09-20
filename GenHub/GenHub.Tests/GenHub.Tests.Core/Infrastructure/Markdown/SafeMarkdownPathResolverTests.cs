using GenHub.Core.Constants;
using GenHub.Infrastructure.Markdown;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
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
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        }));
        var resolver = new SafeMarkdownPathResolver(httpClient);

        // Act
        using var stream = await resolver.ResolveImageResource("https://example.test/shot.png")!;

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
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentLength = 20 * 1024 * 1024;
            return response;
        }));
        var resolver = new SafeMarkdownPathResolver(httpClient);

        // Act
        var stream = await resolver.ResolveImageResource("https://example.test/huge.png")!;

        // Assert
        Assert.Null(stream);
    }

    /// <summary>
    /// Verifies that loopback, private, and link-local destinations are rejected before any network request.
    /// </summary>
    /// <param name="url">The unsafe image URL to resolve.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("http://127.0.0.1/shot.png")]
    [InlineData("http://localhost/shot.png")]
    [InlineData("http://10.0.0.5/shot.png")]
    [InlineData("http://192.168.1.1/shot.png")]
    [InlineData("http://172.16.0.9/shot.png")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/shot.png")]
    [InlineData("http://[fe80::1]/shot.png")]
    public async Task ResolveImageResource_UnsafeDestination_ReturnsNullWithoutRequestAsync(string url)
    {
        // Arrange
        int requestCount = 0;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var resolver = new SafeMarkdownPathResolver(httpClient);

        // Act
        var stream = await resolver.ResolveImageResource(url)!;

        // Assert
        Assert.Null(stream);
        Assert.Equal(0, requestCount);
    }

    /// <summary>
    /// Verifies that redirects targeting private or link-local destinations are rejected.
    /// </summary>
    /// <param name="initialUrl">The initial safe image URL.</param>
    /// <param name="redirectTarget">The unsafe redirect target.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("http://example.test/redirect.png", "http://10.0.0.5/secret.png")]
    [InlineData("https://example.test/redirect.png", "https://192.168.0.9/secret.png")]
    [InlineData("https://example.test/redirect.png", "https://169.254.169.254/latest/meta-data")]
    public async Task ResolveImageResource_RedirectToPrivateDestination_ReturnsNullAsync(string initialUrl, string redirectTarget)
    {
        // Arrange
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri(redirectTarget, UriKind.Absolute) },
        }));
        var resolver = new SafeMarkdownPathResolver(httpClient);

        // Act
        var stream = await resolver.ResolveImageResource(initialUrl)!;

        // Assert
        Assert.Null(stream);
    }

    /// <summary>
    /// Verifies that redirects downgrading from HTTPS to HTTP are rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveImageResource_HttpsToHttpRedirect_ReturnsNullAsync()
    {
        // Arrange
        using var httpClient = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Found)
        {
            Headers = { Location = new Uri("http://example.test/img.png", UriKind.Absolute) },
        }));
        var resolver = new SafeMarkdownPathResolver(httpClient);

        // Act
        var stream = await resolver.ResolveImageResource("https://example.test/redirect.png")!;

        // Assert
        Assert.Null(stream);
    }

    /// <summary>
    /// Verifies that redirects to safe destinations are followed and resolve to exact bytes.
    /// </summary>
    /// <param name="location">The redirect location, absolute or relative.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("https://cdn.example.test/img.png")]
    [InlineData("/img.png")]
    public async Task ResolveImageResource_RedirectToSafeDestination_FollowsRedirectAsync(string location)
    {
        // Arrange
        var payload = Encoding.UTF8.GetBytes("redirected-image-bytes");
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/redirect.png")
            {
                return new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) },
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            };
        }));
        var resolver = new SafeMarkdownPathResolver(httpClient);

        // Act
        using var stream = await resolver.ResolveImageResource("https://example.test/redirect.png")!;

        // Assert
        var memory = Assert.IsType<MemoryStream>(stream);
        Assert.Equal(payload, memory.ToArray());
    }

    /// <summary>
    /// Verifies that endless redirect chains are abandoned after the maximum hop count.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ResolveImageResource_ExcessiveRedirects_ReturnsNullAsync()
    {
        // Arrange
        int requestCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.Found)
            {
                Headers = { Location = new Uri("https://example.test/next.png", UriKind.Absolute) },
            };
        }));
        var resolver = new SafeMarkdownPathResolver(httpClient);

        // Act
        var stream = await resolver.ResolveImageResource("https://example.test/redirect.png")!;

        // Assert
        Assert.Null(stream);
        Assert.Equal(ImageCacheConstants.MaxRedirects + 1, requestCount);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
