using GenHub.Features.Tools.Services.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="DropboxHostingProvider"/> upload retry behavior.
/// </summary>
public sealed class DropboxHostingProviderTests
{
    private sealed class DropboxStubHandler : HttpMessageHandler
    {
        public int UploadCalls { get; private set; }

        public List<byte[]> UploadBodies { get; } = new();

        public bool DisposeRequestContent { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri.Contains("/files/upload", StringComparison.Ordinal))
            {
                UploadCalls++;
                var body = request.Content == null
                    ? Array.Empty<byte>()
                    : await request.Content.ReadAsByteArrayAsync(cancellationToken);
                UploadBodies.Add(body);
                if (DisposeRequestContent)
                {
                    request.Content?.Dispose();
                }

                if (UploadCalls == 1)
                {
                    return JsonResponse(HttpStatusCode.Unauthorized, "{\"error_summary\": \"expired_access_token/\"}");
                }

                return JsonResponse(HttpStatusCode.OK, $"{{\"id\": \"id:uploaded\", \"size\": {body.Length}}}");
            }

            if (uri.Contains("oauth2/token", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, "{\"access_token\": \"fresh-token\", \"token_type\": \"bearer\", \"expires_in\": 14400}");
            }

            if (uri.Contains("list_shared_links", StringComparison.Ordinal))
            {
                return JsonResponse(HttpStatusCode.OK, "{\"links\": [{\"url\": \"https://www.dropbox.com/s/abc123/file.zip?dl=0\"}]}");
            }

            return JsonResponse(HttpStatusCode.NotFound, "{}");
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class NonSeekableReadStream : MemoryStream
    {
        public NonSeekableReadStream(byte[] buffer)
            : base(buffer, writable: false)
        {
        }

        public override bool CanSeek => false;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }

    private sealed class FakeLengthStream : MemoryStream
    {
        private readonly long _fakeLength;

        public FakeLengthStream(long fakeLength)
        {
            _fakeLength = fakeLength;
        }

        public override long Length => _fakeLength;
    }

    /// <summary>
    /// An expired access token must trigger a refresh followed by a retry that re-sends
    /// the exact same payload, even when the first attempt disposes the request content.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadFileAsync_ExpiredToken_RefreshesAndRetriesWithIdenticalPayloadAsync()
    {
        var handler = new DropboxStubHandler { DisposeRequestContent = true };
        using var provider = CreateProvider(handler);
        provider.SetOAuthCredentials(new DropboxOAuthCredential(
            "test-app-key",
            "stale-access-token",
            "test-refresh-token",
            DateTime.UtcNow.AddHours(1)));

        var payload = Encoding.UTF8.GetBytes("dropbox retry payload");
        using var stream = new MemoryStream(payload);

        var result = await provider.UploadFileAsync(stream, "file.zip", cancellationToken: CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("id:uploaded", result.Data.FileId);
        Assert.Equal(2, handler.UploadCalls);
        Assert.Equal(2, handler.UploadBodies.Count);
        Assert.Equal(payload, handler.UploadBodies[0]);
        Assert.Equal(payload, handler.UploadBodies[1]);
    }

    /// <summary>
    /// The expired-token retry must also work for non-seekable caller streams,
    /// which can never be rewound for a second attempt.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadFileAsync_ExpiredTokenWithNonSeekableStream_RetriesSuccessfullyAsync()
    {
        var handler = new DropboxStubHandler { DisposeRequestContent = true };
        using var provider = CreateProvider(handler);
        provider.SetOAuthCredentials(new DropboxOAuthCredential(
            "test-app-key",
            "stale-access-token",
            "test-refresh-token",
            DateTime.UtcNow.AddHours(1)));

        var payload = Encoding.UTF8.GetBytes("non-seekable retry payload");
        using var stream = new NonSeekableReadStream(payload);

        var result = await provider.UploadFileAsync(stream, "file.zip", cancellationToken: CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(2, handler.UploadCalls);
        Assert.Equal(payload, handler.UploadBodies[1]);
    }

    /// <summary>
    /// Files larger than the 150 MB single-request limit must still be rejected
    /// without contacting the upload endpoint.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task UploadFileAsync_OversizeFile_ReturnsLimitFailureWithoutUploadingAsync()
    {
        var handler = new DropboxStubHandler();
        using var provider = CreateProvider(handler);
        provider.SetOAuthCredentials(new DropboxOAuthCredential(
            "test-app-key",
            "stale-access-token",
            "test-refresh-token",
            DateTime.UtcNow.AddHours(1)));

        using var stream = new FakeLengthStream(DropboxHostingProvider.MaxFileSizeBytes + 1);

        var result = await provider.UploadFileAsync(stream, "huge.zip", cancellationToken: CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("150 MB", result.FirstError, StringComparison.Ordinal);
        Assert.Equal(0, handler.UploadCalls);
    }

    private static DropboxHostingProvider CreateProvider(DropboxStubHandler handler)
    {
        var loggerMock = new Mock<ILogger<DropboxHostingProvider>>();
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        return new DropboxHostingProvider(loggerMock.Object, factoryMock.Object);
    }
}
