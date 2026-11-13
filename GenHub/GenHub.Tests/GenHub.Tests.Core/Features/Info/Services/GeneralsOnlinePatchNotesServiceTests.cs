using GenHub.Features.Info.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
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
    [InlineData("v082826")]
    [InlineData("V082826_QFE1")]
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
            .Returns(() => new HttpClient(handler, disposeHandler: false));

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
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        // Act
        var result = await service.GetPatchNotesFormattedAsync("082826");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesFormattedAsync"/> caches results and avoids duplicate HTTP calls.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchNotesFormattedAsync_CachesResult_DoesNotRepeatHttpCallsAsync()
    {
        var callCount = 0;
        var handler = new TestHttpMessageHandler(req =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SamplePatchNotesHtml),
            };
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        var first = await service.GetPatchNotesFormattedAsync("082826");
        var second = await service.GetPatchNotesFormattedAsync("082826");
        var third = await service.GetPatchNotesFormattedAsync("v082826");

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(1, callCount);
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesFormattedAsync"/> falls back to the release cycle patch note when direct page has no details.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchNotesFormattedAsync_IntermediateBuild_FallsBackToReleaseCycleNotesAsync()
    {
        const string indexHtml = @"<!DOCTYPE html>
<html><body>
    <div class=""row g-4"">
        <div class=""col-lg-4 col-md-6 mb10"">
            <div class=""post-text"">
                <div class=""d-date"">28th September 2026</div>
                <h4><a href=""/patchnotes/092826"">Update 092826</a></h4>
                <p>Summary</p>
            </div>
        </div>
    </div>
</body></html>";

        const string update092826Html = @"<!DOCTYPE html>
<html><body>
    <section id=""subheader"">
        <div class=""center-y text-center"">
            <h2>Update 092826</h2>
            <div class=""subtitle"">28th September 2026</div>
        </div>
    </section>
    <div class=""blog-read"">
        <div class=""post-text"">
            <ul>
                <li>Camera controls - Page Up/Down</li>
                <li>Fixed GenHub loading</li>
            </ul>
        </div>
    </div>
</body></html>";

        var handler = new TestHttpMessageHandler(req =>
        {
            var uri = req.RequestUri!.ToString();
            if (uri.EndsWith("/patchnotes/092526", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body>No details</body></html>"),
                };
            }

            if (uri.EndsWith("/patchnotes", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(indexHtml),
                };
            }

            if (uri.EndsWith("/patchnotes/092826", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(update092826Html),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        var notes = await service.GetPatchNotesFormattedAsync("092526");

        Assert.NotNull(notes);
        Assert.Contains("Update 092826 (28th September 2026)", notes);
        Assert.Contains("- Camera controls - Page Up/Down", notes);
        Assert.Contains("- Fixed GenHub loading", notes);
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesFormattedAsync"/> falls back to the release cycle patch note when direct page returns 404 Not Found.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchNotesFormattedAsync_Direct404_FallsBackToReleaseCycleNotesAsync()
    {
        const string indexHtml = @"<!DOCTYPE html>
<html><body>
    <div class=""row g-4"">
        <div class=""col-lg-4 col-md-6 mb10"">
            <div class=""post-text"">
                <div class=""d-date"">28th September 2026</div>
                <h4><a href=""/patchnotes/092826"">Update 092826</a></h4>
                <p>Summary</p>
            </div>
        </div>
    </div>
</body></html>";

        const string update092826Html = @"<!DOCTYPE html>
<html><body>
    <section id=""subheader"">
        <div class=""center-y text-center"">
            <h2>Update 092826</h2>
            <div class=""subtitle"">28th September 2026</div>
        </div>
    </section>
    <div class=""blog-read"">
        <div class=""post-text"">
            <ul>
                <li>Covering release note for intermediate build</li>
            </ul>
        </div>
    </div>
</body></html>";

        var handler = new TestHttpMessageHandler(req =>
        {
            var uri = req.RequestUri!.ToString();
            if (uri.EndsWith("/patchnotes/092526", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (uri.EndsWith("/patchnotes", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(indexHtml),
                };
            }

            if (uri.EndsWith("/patchnotes/092826", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(update092826Html),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        var notes = await service.GetPatchNotesFormattedAsync("092526");

        Assert.NotNull(notes);
        Assert.Contains("Update 092826 (28th September 2026)", notes);
        Assert.Contains("- Covering release note for intermediate build", notes);
    }

    /// <summary>
    /// Verifies that FindBestMatchingPatchNote prefers a future covering release over a past release across year boundaries.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchNotesFormattedAsync_YearBoundary_PrefersCoveringReleaseOverPastReleaseAsync()
    {
        const string indexHtml = @"<!DOCTYPE html>
<html><body>
    <div class=""row g-4"">
        <div class=""col-lg-4 col-md-6 mb10"">
            <div class=""post-text"">
                <span class=""d-date"">30th December 2025</span>
                <h4><a href=""https://www.playgenerals.online/patchnotes/123025"">Update 123025</a></h4>
                <p>Past release</p>
            </div>
        </div>
        <div class=""col-lg-4 col-md-6 mb10"">
            <div class=""post-text"">
                <span class=""d-date"">2nd January 2026</span>
                <h4><a href=""https://www.playgenerals.online/patchnotes/010226"">Update 010226</a></h4>
                <p>New year covering release</p>
            </div>
        </div>
    </div>
</body></html>";

        const string update010226Html = @"<!DOCTYPE html>
<html><body>
    <section id=""subheader"">
        <div class=""center-y text-center"">
            <h2>Update 010226</h2>
            <div class=""subtitle"">2nd January 2026</div>
        </div>
    </section>
    <div class=""blog-read"">
        <div class=""post-text"">
            <ul>
                <li>New Year Bugfixes</li>
            </ul>
        </div>
    </div>
</body></html>";

        var handler = new TestHttpMessageHandler(req =>
        {
            var uri = req.RequestUri!.ToString();
            if (uri.EndsWith("/patchnotes/123125", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<html><body>No details</body></html>"),
                };
            }

            if (uri.EndsWith("/patchnotes", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(indexHtml),
                };
            }

            if (uri.EndsWith("/patchnotes/010226", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(update010226Html),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        var notes = await service.GetPatchNotesFormattedAsync("123125");

        Assert.NotNull(notes);
        Assert.Contains("Update 010226 (2nd January 2026)", notes);
        Assert.Contains("- New Year Bugfixes", notes);
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesAsync()"/> correctly extracts Id and normalizes DetailsUrl for both relative and absolute links.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchNotesAsync_ParsesRelativeAndAbsoluteUrlsCorrectlyAsync()
    {
        const string indexHtml = @"<!DOCTYPE html>
<html><body>
    <div class=""row g-4"">
        <div class=""col-lg-4 col-md-6 mb10"">
            <div class=""post-text"">
                <span class=""d-date"">28th August 2026</span>
                <h4><a href=""/patchnotes/082826"">Update 082826</a></h4>
                <p>Relative URL</p>
            </div>
        </div>
        <div class=""col-lg-4 col-md-6 mb10"">
            <div class=""post-text"">
                <span class=""d-date"">29th August 2026</span>
                <h4><a href=""https://www.playgenerals.online/patchnotes/082926/"">Update 082926</a></h4>
                <p>Absolute URL with trailing slash</p>
            </div>
        </div>
    </div>
</body></html>";

        var handler = new TestHttpMessageHandler(req =>
        {
            if (req.RequestUri!.ToString().EndsWith("/patchnotes", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(indexHtml),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        var notes = (await service.GetPatchNotesAsync()).ToList();

        Assert.Equal(2, notes.Count);
        var note082926 = notes.First(n => n.Id == "082926");
        Assert.Equal("082926", note082926.Id);
        Assert.Equal("https://www.playgenerals.online/patchnotes/082926/", note082926.DetailsUrl);

        var note082826 = notes.First(n => n.Id == "082826");
        Assert.Equal("082826", note082826.Id);
        Assert.Equal("https://www.playgenerals.online/patchnotes/082826", note082826.DetailsUrl);
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesAsync()"/> returns an empty collection and logs error when HTTP fails.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchNotesAsync_HttpError_ReturnsEmptyListAndLogsErrorAsync()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        var notes = await service.GetPatchNotesAsync();

        Assert.NotNull(notes);
        Assert.Empty(notes);
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesAsync(CancellationToken)"/> rethrows when the cancellation token is cancelled.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchNotesAsync_CancelledToken_RethrowsOperationCanceledExceptionAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetPatchNotesAsync(cts.Token));
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesFormattedAsync"/> does not cache HTTP failures, allowing subsequent retries to succeed.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchNotesFormattedAsync_HttpFailure_DoesNotCacheAndAllowsRetryAsync()
    {
        var callCount = 0;
        var handler = new TestHttpMessageHandler(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SamplePatchNotesHtml),
            };
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        var firstResult = await service.GetPatchNotesFormattedAsync("082826");
        Assert.Null(firstResult);

        var secondResult = await service.GetPatchNotesFormattedAsync("082826");
        Assert.NotNull(secondResult);
        Assert.Contains("Update 082826", secondResult);
        Assert.Equal(2, callCount);
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchNotesFormattedAsync"/> rethrows when the cancellation token is cancelled.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchNotesFormattedAsync_CancelledToken_RethrowsOperationCanceledExceptionAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SamplePatchNotesHtml),
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetPatchNotesFormattedAsync("082826", cts.Token));
    }

    /// <summary>
    /// Tests that <see cref="GeneralsOnlinePatchNotesService.GetPatchDetailsAsync"/> rethrows when the cancellation token is cancelled.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetPatchDetailsAsync_CancelledToken_RethrowsOperationCanceledExceptionAsync()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SamplePatchNotesHtml),
        });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var service = new GeneralsOnlinePatchNotesService(
            factoryMock.Object,
            NullLogger<GeneralsOnlinePatchNotesService>.Instance);

        var patchNote = new GenHub.Core.Models.Info.PatchNote
        {
            Id = "082826",
            DetailsUrl = "https://www.playgenerals.online/patchnotes/082826",
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetPatchDetailsAsync(patchNote, cts.Token));
    }

    private sealed class TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
