using GenHub.Core.Constants;
using GenHub.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Services;

/// <summary>
/// Unit tests for <see cref="RemoteFileSizeProbe"/> redirect handling and payload guards.
/// </summary>
public sealed class RemoteFileSizeProbeTests
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Verifies that a direct HEAD response with a payload content type returns its length.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TryProbeSizeAsync_DirectDownload_ReturnsContentLength()
    {
        using var client = CreateClient(new HeadResponse(HttpStatusCode.OK, 211647919, "application/zip"));

        var size = await RemoteFileSizeProbe.TryProbeSizeAsync(client, "https://cdn.example.com/mod.zip", ProbeTimeout);

        Assert.Equal(211647919, size);
    }

    /// <summary>
    /// Verifies that an http to https redirect (as served by gen.insave.ovh mirrors) is followed.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TryProbeSizeAsync_RedirectToHttps_FollowsRedirect()
    {
        using var client = new HttpClient(new QueueHandler(
        [
            HeadRedirect("https://cdn.example.com/mod.zip"),
            new HeadResponse(HttpStatusCode.OK, 551417137, "application/zip"),
        ]));

        var size = await RemoteFileSizeProbe.TryProbeSizeAsync(client, "http://cdn.example.com/mod.zip", ProbeTimeout);

        Assert.Equal(551417137, size);
    }

    /// <summary>
    /// Verifies that relative redirect targets resolve against the request URI.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TryProbeSizeAsync_RelativeRedirect_ResolvesAgainstRequestUri()
    {
        using var client = new HttpClient(new QueueHandler(
        [
            HeadRedirect("/files/mod.zip"),
            new HeadResponse(HttpStatusCode.OK, 1024, "application/zip"),
        ]));

        var size = await RemoteFileSizeProbe.TryProbeSizeAsync(client, "http://cdn.example.com/download", ProbeTimeout);

        Assert.Equal(1024, size);
    }

    /// <summary>
    /// Verifies that descriptor documents are never reported as download sizes.
    /// </summary>
    /// <param name="url">The descriptor URL to probe.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("https://raw.githubusercontent.com/test/mod/main/mod.yaml")]
    [InlineData("https://raw.githubusercontent.com/test/mod/main/mod.yml")]
    [InlineData("https://example.com/manifest.txt")]
    [InlineData("https://example.com/manifest.json")]
    public async Task TryProbeSizeAsync_DescriptorUrl_ReturnsNullWithoutRequest(string url)
    {
        var handler = new QueueHandler([new HeadResponse(HttpStatusCode.OK, 552, "text/plain")]);
        using var client = new HttpClient(handler);

        var size = await RemoteFileSizeProbe.TryProbeSizeAsync(client, url, ProbeTimeout);

        Assert.Null(size);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// Verifies that non-payload content types are rejected even with a Content-Length.
    /// </summary>
    /// <param name="mediaType">The response media type.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("application/json")]
    public async Task TryProbeSizeAsync_NonPayloadMediaType_ReturnsNull(string mediaType)
    {
        using var client = CreateClient(new HeadResponse(HttpStatusCode.OK, 552, mediaType));

        var size = await RemoteFileSizeProbe.TryProbeSizeAsync(client, "https://cdn.example.com/mod.zip", ProbeTimeout);

        Assert.Null(size);
    }

    /// <summary>
    /// Verifies that redirect loops terminate with an unknown size.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TryProbeSizeAsync_RedirectLoop_ReturnsNull()
    {
        var redirects = new List<HeadResponse>();
        for (var i = 0; i <= ContentConstants.MaxSizeProbeRedirects + 1; i++)
        {
            redirects.Add(HeadRedirect("http://cdn.example.com/loop"));
        }

        using var client = new HttpClient(new QueueHandler(redirects));

        var size = await RemoteFileSizeProbe.TryProbeSizeAsync(client, "http://cdn.example.com/loop", ProbeTimeout);

        Assert.Null(size);
    }

    /// <summary>
    /// Verifies that redirects to unsafe hosts are rejected.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TryProbeSizeAsync_RedirectToUnsafeHost_ReturnsNull()
    {
        using var client = new HttpClient(new QueueHandler(
        [
            HeadRedirect("http://localhost/mod.zip"),
            new HeadResponse(HttpStatusCode.OK, 1024, "application/zip"),
        ]));

        var size = await RemoteFileSizeProbe.TryProbeSizeAsync(client, "http://cdn.example.com/mod.zip", ProbeTimeout);

        Assert.Null(size);
    }

    /// <summary>
    /// Verifies that unsafe initial URLs are rejected without a request.
    /// </summary>
    /// <param name="url">The unsafe URL to probe.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ftp://cdn.example.com/mod.zip")]
    [InlineData("http://localhost/mod.zip")]
    [InlineData("http://127.0.0.1/mod.zip")]
    public async Task TryProbeSizeAsync_UnsafeUrl_ReturnsNull(string? url)
    {
        var handler = new QueueHandler([new HeadResponse(HttpStatusCode.OK, 1024, "application/zip")]);
        using var client = new HttpClient(handler);

        var size = await RemoteFileSizeProbe.TryProbeSizeAsync(client, url, ProbeTimeout);

        Assert.Null(size);
        Assert.Equal(0, handler.RequestCount);
    }

    /// <summary>
    /// Verifies that cooperative cancellation from the caller is propagated.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task TryProbeSizeAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        using var client = CreateClient(new HeadResponse(HttpStatusCode.OK, 1024, "application/zip"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RemoteFileSizeProbe.TryProbeSizeAsync(client, "https://cdn.example.com/mod.zip", ProbeTimeout, cts.Token));
    }

    private static HttpClient CreateClient(HeadResponse response)
    {
        return new HttpClient(new QueueHandler([response]));
    }

    private static HeadResponse HeadRedirect(string location)
    {
        return new HeadResponse(HttpStatusCode.MovedPermanently, null, null, location);
    }

    private sealed record HeadResponse(
        HttpStatusCode StatusCode,
        long? ContentLength,
        string? MediaType,
        string? Location = null);

    private sealed class QueueHandler(List<HeadResponse> responses) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var index = Math.Min(RequestCount, responses.Count - 1);
            RequestCount++;

            var planned = responses[index];
            var message = new HttpResponseMessage(planned.StatusCode)
            {
                Content = new ByteArrayContent([]),
                RequestMessage = request,
            };

            if (planned.ContentLength.HasValue)
            {
                message.Content.Headers.ContentLength = planned.ContentLength.Value;
            }

            if (!string.IsNullOrEmpty(planned.MediaType))
            {
                message.Content.Headers.ContentType = new MediaTypeHeaderValue(planned.MediaType);
            }

            if (!string.IsNullOrEmpty(planned.Location))
            {
                message.Headers.Location = new Uri(planned.Location, UriKind.RelativeOrAbsolute);
            }

            return Task.FromResult(message);
        }
    }
}
