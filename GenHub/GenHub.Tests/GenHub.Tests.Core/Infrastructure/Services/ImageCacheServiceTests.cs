using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.Services;
using Moq;
using Moq.Protected;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Services;

/// <summary>
/// Unit tests for <see cref="ImageCacheService"/> security, caching, and download logic.
/// </summary>
public class ImageCacheServiceTests
{
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>
    /// Verifies that private, loopback, multicast, and link-local IPv4 addresses are rejected.
    /// </summary>
    /// <param name="ipString">The IPv4 string to test.</param>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("169.254.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.255")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    public void IsSafeRemoteUrl_UnsafeIPv4_ReturnsFalse(string ipString)
    {
        var url = $"http://{ipString}/image.png";
        Assert.False(ImageCacheService.IsSafeRemoteUrl(url, out _));
    }

    /// <summary>
    /// Verifies that private, loopback, multicast, unique-local, and IPv4-mapped IPv6 addresses are rejected.
    /// Bracketed syntax is required for IPv6 host formatting in URIs.
    /// </summary>
    /// <param name="ipString">The IPv6 string to test.</param>
    [Theory]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a:1::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:192.168.0.1")]
    public void IsSafeRemoteUrl_UnsafeIPv6_ReturnsFalse(string ipString)
    {
        var url = $"http://[{ipString}]/image.png";
        Assert.False(ImageCacheService.IsSafeRemoteUrl(url, out _));
    }

    /// <summary>
    /// Verifies that localhost, local hostname shortcuts, and reserved internal names are rejected.
    /// </summary>
    /// <param name="host">The host name to test.</param>
    [Theory]
    [InlineData("localhost")]
    [InlineData("myhost.local")]
    [InlineData("service.internal")]
    public void IsSafeRemoteUrl_InternalHostnames_ReturnsFalse(string host)
    {
        var url = $"http://{host}/test.png";
        Assert.False(ImageCacheService.IsSafeRemoteUrl(url, out _));
    }

    /// <summary>
    /// Verifies that non-HTTP/HTTPS schemes (such as file, ftp, javascript) are rejected as remote URLs.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    [Theory]
    [InlineData("ftp://example.com/test.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,abc")]
    [InlineData("not-a-valid-url")]
    public void IsSafeRemoteUrl_NonHttpSchemes_ReturnsFalse(string url)
    {
        Assert.False(ImageCacheService.IsSafeRemoteUrl(url, out _));
    }

    /// <summary>
    /// Verifies that valid public web URLs pass the remote URL safety check.
    /// </summary>
    /// <param name="url">The valid URL to test.</param>
    [Theory]
    [InlineData("https://media.moddb.com/images/mods/1/2/test.jpg")]
    [InlineData("http://example.com/test.png")]
    [InlineData("https://8.8.8.8/test.png")]
    public void IsSafeRemoteUrl_SafeUrls_ReturnsTrue(string url)
    {
        Assert.True(ImageCacheService.IsSafeRemoteUrl(url, out var uri));
        Assert.NotNull(uri);
    }

    /// <summary>
    /// Verifies that <see cref="ImageCacheService"/> initializes using the path from
    /// <see cref="IConfigurationProviderService.GetApplicationDataPath"/>.
    /// </summary>
    [Fact]
    public void Constructor_WithConfigurationProvider_UsesRelocatedPath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "GenHubTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var configMock = new Mock<IConfigurationProviderService>();
            configMock.Setup(c => c.GetApplicationDataPath()).Returns(tempRoot);

            var service = new ImageCacheService(configMock.Object);
            var expectedCacheDir = Path.Combine(tempRoot, "Images");

            Assert.True(Directory.Exists(expectedCacheDir));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // ignore cleanup failure
                }
            }
        }
    }

    /// <summary>
    /// Verifies that local image files can be loaded via <see cref="ImageCacheService.GetBitmapAsync"/>
    /// without locking the file on disk.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task GetBitmapAsync_LocalFile_LoadsSuccessfullyWithoutLockingAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_local_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, $"test_image_{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(tempFile, ValidPngBytes);

        try
        {
            var configMock = new Mock<IConfigurationProviderService>();
            configMock.Setup(c => c.GetApplicationDataPath()).Returns(tempDir);

            var service = new ImageCacheService(configMock.Object);
            var bitmap = await service.GetBitmapAsync(tempFile);

            Assert.NotNull(bitmap);
            Assert.Equal(1, bitmap.PixelSize.Width);
            Assert.Equal(1, bitmap.PixelSize.Height);

            // Verify file is not locked on disk by modifying or deleting it immediately
            File.WriteAllBytes(tempFile, ValidPngBytes);
            File.Delete(tempFile);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // ignore cleanup failure
                }
            }
        }
    }

    /// <summary>
    /// Verifies that memory cache eviction does not dispose bitmaps currently in use.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task MemoryCache_Eviction_DoesNotDisposeBitmapAsync()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var res = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(ValidPngBytes),
                };
                res.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                return res;
            });

        var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_eviction_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var configMock = new Mock<IConfigurationProviderService>();
            configMock.Setup(c => c.GetApplicationDataPath()).Returns(tempDir);

            var httpClient = new HttpClient(handlerMock.Object);
            var service = new ImageCacheService(configMock.Object, httpClient);

            // Fetch the first image which will be in memory cache
            var bitmap0 = await service.GetBitmapAsync($"https://example.com/first_image_{Guid.NewGuid():N}.png");
            Assert.NotNull(bitmap0);

            // Flood cache with 205 items to trigger LRU eviction (MaxMemoryCacheEntries = 200)
            for (int i = 0; i < 205; i++)
            {
                await service.GetBitmapAsync($"https://example.com/flood_{Guid.NewGuid():N}.png");
            }

            // Also test ClearMemoryCache
            service.ClearMemoryCache();

            // Bitmap0 should still be usable and not throw ObjectDisposedException
            Assert.Equal(1, bitmap0.PixelSize.Width);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Ignore cleanup failure in test teardown
                }
            }
        }
    }

    /// <summary>
    /// Verifies that multiple concurrent calls to <see cref="ImageCacheService.GetBitmapAsync"/>
    /// for the same URL coalesce into a single HTTP download.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task GetBitmapAsync_ConcurrentRequests_CoalesceToSingleDownloadAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_coalesce_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var configMock = new Mock<IConfigurationProviderService>();
            configMock.Setup(c => c.GetApplicationDataPath()).Returns(tempDir);

            int requestCount = 0;
            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .Returns(async (HttpRequestMessage req, CancellationToken ct) =>
                {
                    Interlocked.Increment(ref requestCount);
                    await Task.Delay(50, ct); // simulate network latency
                    var res = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(ValidPngBytes),
                    };
                    res.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return res;
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var service = new ImageCacheService(configMock.Object, httpClient);

            var url = $"https://example.com/test_coalesce_{Guid.NewGuid():N}.png";

            // Launch 5 concurrent requests
            var task1 = service.GetBitmapAsync(url);
            var task2 = service.GetBitmapAsync(url);
            var task3 = service.GetBitmapAsync(url);
            var task4 = service.GetBitmapAsync(url);
            var task5 = service.GetBitmapAsync(url);

            var results = await Task.WhenAll(task1, task2, task3, task4, task5);

            foreach (var r in results)
            {
                Assert.NotNull(r);
            }

            // Only 1 actual HTTP request should have executed
            Assert.Equal(1, requestCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // ignore cleanup failure
                }
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="ImageCacheService.GetBitmapAsync"/> follows HTTP redirects.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task GetBitmapAsync_FollowsRedirectAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_redirect_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var configMock = new Mock<IConfigurationProviderService>();
            configMock.Setup(c => c.GetApplicationDataPath()).Returns(tempDir);

            var redirectKey = Guid.NewGuid().ToString("N");
            var redirectUrl = $"https://example.com/redirect_{redirectKey}.png";
            var destUrl = $"https://example.com/dest_{redirectKey}.png";

            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == redirectUrl),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri(destUrl);
                    return redirect;
                });

            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == destUrl),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    var ok = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(ValidPngBytes),
                    };
                    ok.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                    return ok;
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var service = new ImageCacheService(configMock.Object, httpClient);

            var bitmap = await service.GetBitmapAsync(redirectUrl);
            Assert.NotNull(bitmap);
            Assert.Equal(1, bitmap.PixelSize.Width);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // ignore cleanup failure
                }
            }
        }
    }

    /// <summary>
    /// Verifies that non-image content types (such as HTML error pages) are rejected and return null.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task GetBitmapAsync_NonImageContentType_ReturnsNullAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_error_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var configMock = new Mock<IConfigurationProviderService>();
            configMock.Setup(c => c.GetApplicationDataPath()).Returns(tempDir);

            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    var res = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("<html><body>Not an image</body></html>"),
                    };
                    res.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
                    return res;
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var service = new ImageCacheService(configMock.Object, httpClient);

            var bitmap = await service.GetBitmapAsync($"https://example.com/error_{Guid.NewGuid():N}.png");
            Assert.Null(bitmap);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // ignore cleanup failure
                }
            }
        }
    }

    /// <summary>
    /// Verifies that <see cref="ImageCacheService.GetBitmapAsync"/> blocks redirect downgrade from HTTPS to HTTP.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task GetBitmapAsync_BlocksHttpsToHttpRedirectDowngradeAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_redirect_downgrade_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var configMock = new Mock<IConfigurationProviderService>();
            configMock.Setup(c => c.GetApplicationDataPath()).Returns(tempDir);

            var redirectKey = Guid.NewGuid().ToString("N");
            var redirectUrl = $"https://example.com/secure_{redirectKey}.png";
            var destUrl = $"http://example.com/insecure_{redirectKey}.png";

            var handlerMock = new Mock<HttpMessageHandler>();
            handlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == redirectUrl),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() =>
                {
                    var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
                    redirect.Headers.Location = new Uri(destUrl);
                    return redirect;
                });

            var httpClient = new HttpClient(handlerMock.Object);
            var service = new ImageCacheService(configMock.Object, httpClient);

            var bitmap = await service.GetBitmapAsync(redirectUrl);
            Assert.Null(bitmap);

            handlerMock.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == destUrl),
                ItExpr.IsAny<CancellationToken>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // ignore cleanup failure
                }
            }
        }
    }

    /// <summary>
    /// Verifies that UNC network share paths are rejected by <see cref="ImageCacheService.GetBitmapAsync"/>.
    /// </summary>
    /// <param name="uncPath">The UNC path to test.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(@"\\attacker\share\image.png")]
    [InlineData("//attacker/share/image.png")]
    [InlineData("file:////attacker/share/image.png")]
    public async Task GetBitmapAsync_RejectsUncPathsAsync(string uncPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"genhub_unc_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var configMock = new Mock<IConfigurationProviderService>();
            configMock.Setup(c => c.GetApplicationDataPath()).Returns(tempDir);
            var service = new ImageCacheService(configMock.Object);

            var bitmap = await service.GetBitmapAsync(uncPath);
            Assert.Null(bitmap);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // ignore cleanup failure
                }
            }
        }
    }
}
