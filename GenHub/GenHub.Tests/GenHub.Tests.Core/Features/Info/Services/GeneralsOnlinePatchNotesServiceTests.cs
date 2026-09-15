using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Features.Info.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Info.Services;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlinePatchNotesService"/>.
/// </summary>
public class GeneralsOnlinePatchNotesServiceTests
{
    private const string SamplePatchNotesHtml = @"<!DOCTYPE html>
<html>
<head><title>Update</title></head>
<body>
    <section id=""subheader"">
        <div class=""center-y text-center"">
            <h2>Update 082826</h2>
            <div class=""subtitle"">28th August 2026</div>
        </div>
    </section>
    <div class=""blog-read"">
        <div class=""post-text"">
            <ul>
                <li>Community Patch v1.0.1</li>
                <li>Fixed a bug where some players cannot establish connection</li>
                <li>Added &#039;tournament&#039; lobby in server list menu</li>
            </ul>
        </div>
    </div>
</body>
</html>";

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesFormattedAsync"/> parses day release and QFE versions correctly.
    /// </summary>
    /// <param name="version">The version to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("082826")]
    [InlineData("082826_QFE1")]
    public async Task GetPatchNotesFormattedAsync_ParsesDayReleaseAndQfeCorrectlyAsync(string version)
    {
        // Arrange
        var handler = new TestHttpMessageHandler(req =>
        {
            Assert.Contains("/patchnotes/082826", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SamplePatchNotesHtml),
            };
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        // Act
        var formatted = await service.GetPatchNotesFormattedAsync(version);

        // Assert
        Assert.NotNull(formatted);
        Assert.Contains("Update 082826 (28th August 2026)", formatted);
        Assert.Contains("- Community Patch v1.0.1", formatted);
        Assert.Contains("- Fixed a bug where some players cannot establish connection", formatted);
        Assert.Contains("- Added 'tournament' lobby in server list menu", formatted);
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesFormattedAsync"/> returns null for invalid version formats.
    /// </summary>
    /// <param name="version">The invalid version to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    public async Task GetPatchNotesFormattedAsync_InvalidVersion_ReturnsNullAsync(string? version)
    {
        // Arrange
        var factoryMock = new Mock<IHttpClientFactory>();
        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        // Act
        var result = await service.GetPatchNotesFormattedAsync(version!);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesFormattedAsync"/> returns null when HTTP fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchNotesFormattedAsync_HttpError_ReturnsNullAsync()
    {
        // Arrange
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        // Act
        var result = await service.GetPatchNotesFormattedAsync("082826");

        // Assert
        Assert.Null(result);
    }

    private sealed class TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
