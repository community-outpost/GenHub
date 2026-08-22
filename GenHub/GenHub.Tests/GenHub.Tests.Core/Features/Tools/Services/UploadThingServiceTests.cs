using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.UploadThing;
using GenHub.Features.Tools.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Tests for the gateway-mediated UploadThingService integration.
/// </summary>
public sealed class UploadThingServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<ILogger<UploadThingService>> _loggerMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="UploadThingServiceTests"/> class.
    /// </summary>
    public UploadThingServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Removes temporary test data.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that UploadFileAsync returns null when the file does not exist.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UploadFileAsync_WhenFileDoesNotExist_ReturnsNullAsync()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var service = new UploadThingService(httpClient, _loggerMock.Object);

        var result = await service.UploadFileAsync(Path.Combine(_tempDirectory, "nonexistent.zip"));

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that UploadFileAsync completes successfully through direct gateway upload.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UploadFileAsync_WhenGatewaySucceeds_ReturnsUploadResultAsync()
    {
        var testFilePath = Path.Combine(_tempDirectory, "test_replay.zip");
        await File.WriteAllBytesAsync(testFilePath, [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00]);

        var uploadResponse = new DirectUploadResponse(
            "https://utfs.io/f/test_key_123",
            "test_key_123",
            "test_key_123:1755820800.hmac_sig");

        var handlerMock = new Mock<HttpMessageHandler>();

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains(ApiConstants.UploadEndpoint)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(uploadResponse)),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new UploadThingService(httpClient, _loggerMock.Object);

        var progressMock = new Mock<IProgress<double>>();
        var result = await service.UploadFileAsync(testFilePath, progressMock.Object);

        Assert.NotNull(result);
        Assert.Equal("https://utfs.io/f/test_key_123", result.PublicUrl);
        Assert.Equal("test_key_123", result.FileKey);
        Assert.Equal("test_key_123:1755820800.hmac_sig", result.DeleteToken);
        progressMock.Verify(p => p.Report(It.IsAny<double>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// Verifies that UploadFileAsync returns null when the gateway rejects the request.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UploadFileAsync_WhenGatewayRejects_ReturnsNullAsync()
    {
        var testFilePath = Path.Combine(_tempDirectory, "oversized.zip");
        await File.WriteAllBytesAsync(testFilePath, [0x50, 0x4B, 0x03, 0x04]);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":\"File size exceeds 10MB limit\"}"),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new UploadThingService(httpClient, _loggerMock.Object);

        var result = await service.UploadFileAsync(testFilePath);

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that UploadFileAsync returns null when the gateway returns incomplete JSON.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UploadFileAsync_WhenIncompleteResponse_ReturnsNullAsync()
    {
        var testFilePath = Path.Combine(_tempDirectory, "partial.zip");
        await File.WriteAllBytesAsync(testFilePath, [0x50, 0x4B, 0x03, 0x04]);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"publicUrl\":\"https://utfs.io/f/partial\"}"),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new UploadThingService(httpClient, _loggerMock.Object);

        var result = await service.UploadFileAsync(testFilePath);

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that UploadFileAsync propagates OperationCanceledException upon cancellation.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UploadFileAsync_WhenCancelled_ThrowsOperationCanceledExceptionAsync()
    {
        var testFilePath = Path.Combine(_tempDirectory, "canceled.zip");
        await File.WriteAllBytesAsync(testFilePath, [0x50, 0x4B, 0x03, 0x04]);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new UploadThingService(httpClient, _loggerMock.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.UploadFileAsync(testFilePath, ct: cts.Token));
    }

    /// <summary>
    /// Verifies that DeleteFileAsync returns true when the gateway accepts the delete request.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteFileAsync_WhenValidKeyAndToken_ReturnsTrueAsync()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains(ApiConstants.UploadDeleteEndpoint)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}"),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new UploadThingService(httpClient, _loggerMock.Object);

        var result = await service.DeleteFileAsync("test_key_123", "test_key_123:1755820800.valid_sig");

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that DeleteFileAsync returns false when the gateway rejects the deletion.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteFileAsync_WhenGatewayRejects_ReturnsFalseAsync()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{\"error\":\"Invalid or forged delete token signature\"}"),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new UploadThingService(httpClient, _loggerMock.Object);

        var result = await service.DeleteFileAsync("test_key_123", "test_key_123:1755820800.invalid_sig");

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that DeleteFileAsync returns false when given empty or whitespace parameters.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData("", "valid_token")]
    [InlineData("valid_key", "")]
    [InlineData("   ", "valid_token")]
    [InlineData("valid_key", "   ")]
    public async Task DeleteFileAsync_WhenMissingParameters_ReturnsFalseAsync(string key, string token)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var service = new UploadThingService(httpClient, _loggerMock.Object);

        var result = await service.DeleteFileAsync(key, token);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that DeleteFileAsync propagates OperationCanceledException upon cancellation.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteFileAsync_WhenCancelled_ThrowsOperationCanceledExceptionAsync()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var httpClient = new HttpClient(handlerMock.Object);
        var service = new UploadThingService(httpClient, _loggerMock.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.DeleteFileAsync("key", "token", ct: cts.Token));
    }
}
