using System.Net;
using System.Threading.Tasks;
using GenHub.Infrastructure.Services;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Services;

/// <summary>
/// Unit tests for <see cref="ImageCacheService"/> security methods.
/// </summary>
public class ImageCacheServiceTests
{
    /// <summary>
    /// Verifies that private and loopback IPv4/IPv6 addresses are rejected as unsafe.
    /// </summary>
    /// <param name="ipString">The IP string to test.</param>
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    public void IsSafeIpAddress_PrivateOrLoopback_ReturnsFalse(string ipString)
    {
        var ip = IPAddress.Parse(ipString);
        Assert.False(ImageCacheService.IsSafeIpAddress(ip));
    }

    /// <summary>
    /// Verifies that public routable IP addresses are accepted as safe.
    /// </summary>
    /// <param name="ipString">The IP string to test.</param>
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("142.250.190.46")]
    [InlineData("2606:4700:4700::1111")]
    public void IsSafeIpAddress_PublicRoutableIp_ReturnsTrue(string ipString)
    {
        var ip = IPAddress.Parse(ipString);
        Assert.True(ImageCacheService.IsSafeIpAddress(ip));
    }

    /// <summary>
    /// Verifies that localhost and invalid hostnames are rejected by <see cref="ImageCacheService.IsSafeHostAsync"/>.
    /// </summary>
    /// <param name="host">The host to test.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("192.168.0.1")]
    [InlineData("")]
    public async Task IsSafeHostAsync_UnsafeHost_ReturnsFalseAsync(string host)
    {
        var result = await ImageCacheService.IsSafeHostAsync(host);
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that non-HTTP/HTTPS and UNC paths are rejected by <see cref="ImageCacheService.IsSafeRemoteUrl"/>.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("custom://example.com/image.png")]
    [InlineData("\\\\server\\share\\image.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://localhost/test.png")]
    [InlineData("https://127.0.0.1/test.png")]
    [InlineData("https://192.168.1.1/test.png")]
    public void IsSafeRemoteUrl_UnsafeUrl_ReturnsFalse(string url)
    {
        var result = ImageCacheService.IsSafeRemoteUrl(url, out _);
        Assert.False(result);
    }

    /// <summary>
    /// Verifies that valid public HTTP/HTTPS URLs are accepted.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    [Theory]
    [InlineData("https://example.com/image.png")]
    [InlineData("https://cdn.playgenerals.online/images/cover.jpg")]
    [InlineData("https://8.8.8.8/image.jpg")]
    public void IsSafeRemoteUrl_SafeUrl_ReturnsTrue(string url)
    {
        var result = ImageCacheService.IsSafeRemoteUrl(url, out var uri);
        Assert.True(result);
        Assert.NotNull(uri);
    }
}
