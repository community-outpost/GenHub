using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Unit tests for <see cref="SsrfSafeHttpHelper"/>.
/// </summary>
public sealed class SsrfSafeHttpHelperTests
{
    /// <summary>
    /// Verifies that a non-redirect response is returned with the initial URI after validation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SendWithValidatedRedirectsAsync_WithoutRedirect_ReturnsResponseAsync()
    {
        var initialUri = new Uri("https://example.com/maps.zip");
        using var handler = new QueueHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("map-bytes") },
        ]);
        using var httpClient = new HttpClient(handler);
        var validator = CreateValidator(true);

        var validated = await SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
            httpClient,
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            initialUri,
            5,
            validator.Object,
            CancellationToken.None);

        using var response = validated.Response;
        var finalUri = validated.FinalUri;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("map-bytes", await response.Content.ReadAsStringAsync(CancellationToken.None));

        Assert.Equal(initialUri, finalUri);
        Assert.Equal([initialUri], handler.RequestedUris);
        validator.Verify(v => v.IsSafeAsync(initialUri, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that relative and absolute redirect targets are followed when each hop validates.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SendWithValidatedRedirectsAsync_WithRedirectChain_FollowsEachValidatedHopAsync()
    {
        var initialUri = new Uri("https://example.com/start");
        var relativeUri = new Uri("https://example.com/step2");
        var absoluteUri = new Uri("https://cdn.example.net/final.zip");
        using var handler = new QueueHandler(
        [
            Redirect(HttpStatusCode.Found, new Uri("/step2", UriKind.Relative)),
            Redirect(HttpStatusCode.MovedPermanently, absoluteUri),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("final-bytes") },
        ]);
        using var httpClient = new HttpClient(handler);
        var validator = CreateValidator(true);

        var validated = await SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
            httpClient,
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            initialUri,
            5,
            validator.Object,
            CancellationToken.None);

        using var response = validated.Response;
        var finalUri = validated.FinalUri;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("final-bytes", await response.Content.ReadAsStringAsync(CancellationToken.None));

        Assert.Equal(absoluteUri, finalUri);
        Assert.Equal([initialUri, relativeUri, absoluteUri], handler.RequestedUris);
        Assert.Equal(3, validator.Invocations.Count);
    }

    /// <summary>
    /// Verifies that an unsafe initial target throws before any request is sent.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SendWithValidatedRedirectsAsync_WithUnsafeInitialTarget_ThrowsWithoutSendingAsync()
    {
        var initialUri = new Uri("http://192.168.1.9/maps.zip");
        using var handler = new QueueHandler(
        [
            new HttpResponseMessage(HttpStatusCode.OK),
        ]);
        using var httpClient = new HttpClient(handler);
        var validator = CreateValidator(false);

        await Assert.ThrowsAsync<HttpRequestException>(() => SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
            httpClient,
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            initialUri,
            5,
            validator.Object,
            CancellationToken.None));

        Assert.Empty(handler.RequestedUris);
    }

    /// <summary>
    /// Verifies that an unsafe redirect target stops the chain after the first request.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SendWithValidatedRedirectsAsync_WithUnsafeRedirectTarget_ThrowsAsync()
    {
        var initialUri = new Uri("https://example.com/start");
        using var handler = new QueueHandler(
        [
            Redirect(HttpStatusCode.Found, new Uri("http://169.254.169.254/latest")),
            new HttpResponseMessage(HttpStatusCode.OK),
        ]);
        using var httpClient = new HttpClient(handler);
        var validator = new Mock<IDownloadUrlValidator>();
        validator.Setup(v => v.IsSafeAsync(initialUri, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        validator.Setup(v => v.IsSafeAsync(It.Is<Uri>(u => u != initialUri), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<HttpRequestException>(() => SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
            httpClient,
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            initialUri,
            5,
            validator.Object,
            CancellationToken.None));

        Assert.Equal([initialUri], handler.RequestedUris);
    }

    /// <summary>
    /// Verifies that non-HTTP(S) redirect targets are rejected.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SendWithValidatedRedirectsAsync_WithNonHttpRedirectTarget_ThrowsAsync()
    {
        var initialUri = new Uri("https://example.com/start");
        using var handler = new QueueHandler(
        [
            Redirect(HttpStatusCode.Found, new Uri("file:///etc/passwd")),
        ]);
        using var httpClient = new HttpClient(handler);
        var validator = CreateValidator(true);

        await Assert.ThrowsAsync<HttpRequestException>(() => SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
            httpClient,
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            initialUri,
            5,
            validator.Object,
            CancellationToken.None));

        Assert.Equal([initialUri], handler.RequestedUris);
    }

    /// <summary>
    /// Verifies that redirect responses without a Location header are rejected.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SendWithValidatedRedirectsAsync_WithMissingLocationHeader_ThrowsAsync()
    {
        var initialUri = new Uri("https://example.com/start");
        using var handler = new QueueHandler(
        [
            new HttpResponseMessage(HttpStatusCode.Found),
        ]);
        using var httpClient = new HttpClient(handler);
        var validator = CreateValidator(true);

        await Assert.ThrowsAsync<HttpRequestException>(() => SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
            httpClient,
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            initialUri,
            5,
            validator.Object,
            CancellationToken.None));
    }

    /// <summary>
    /// Verifies that chains exceeding the hop limit are rejected.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SendWithValidatedRedirectsAsync_WithTooManyRedirects_ThrowsAsync()
    {
        var initialUri = new Uri("https://example.com/start");
        using var handler = new QueueHandler(
        [
            Redirect(HttpStatusCode.Found, new Uri("https://example.com/one")),
            Redirect(HttpStatusCode.Found, new Uri("https://example.com/two")),
            new HttpResponseMessage(HttpStatusCode.OK),
        ]);
        using var httpClient = new HttpClient(handler);
        var validator = CreateValidator(true);

        await Assert.ThrowsAsync<HttpRequestException>(() => SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
            httpClient,
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            initialUri,
            1,
            validator.Object,
            CancellationToken.None));

        Assert.Equal(2, handler.RequestedUris.Count);
    }

    /// <summary>
    /// Verifies that a client which followed redirects automatically is detected.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SendWithValidatedRedirectsAsync_WithAutoRedirectedResponse_ThrowsAsync()
    {
        var initialUri = new Uri("https://example.com/start");
        var smuggledUri = new Uri("https://example.com/smuggled");
        var autoRedirected = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, smuggledUri),
        };
        using var handler = new QueueHandler([autoRedirected]);
        using var httpClient = new HttpClient(handler);
        var validator = CreateValidator(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SsrfSafeHttpHelper.SendWithValidatedRedirectsAsync(
            httpClient,
            static uri => new HttpRequestMessage(HttpMethod.Get, uri),
            initialUri,
            5,
            validator.Object,
            CancellationToken.None));
    }

    private static Mock<IDownloadUrlValidator> CreateValidator(bool result)
    {
        var validator = new Mock<IDownloadUrlValidator>();
        validator.Setup(v => v.IsSafeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
        return validator;
    }

    private static HttpResponseMessage Redirect(HttpStatusCode statusCode, Uri location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.Location = location;
        return response;
    }

    private sealed class QueueHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<Uri> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
