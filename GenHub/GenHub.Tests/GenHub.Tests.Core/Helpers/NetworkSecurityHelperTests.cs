using GenHub.Core.Helpers;
using System.Net;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="NetworkSecurityHelper"/>.
/// </summary>
public class NetworkSecurityHelperTests
{
    /// <summary>
    /// Verifies that safe external HTTPS URLs pass validation.
    /// </summary>
    [Theory]
    [InlineData("https://raw.githubusercontent.com/user/repo/main/catalog.json")]
    [InlineData("https://cdn.example.com/mod/catalog.json")]
    [InlineData("http://example.com/catalog.json")]
    public void IsSafeUrl_ValidExternalUrl_ReturnsTrue(string url)
    {
        var result = NetworkSecurityHelper.IsSafeUrl(url, out var failureReason);

        Assert.True(result);
        Assert.Null(failureReason);
    }

    /// <summary>
    /// Verifies that loopback, local, and private URLs are blocked.
    /// </summary>
    [Theory]
    [InlineData("http://localhost/catalog.json")]
    [InlineData("http://127.0.0.1/catalog.json")]
    [InlineData("http://127.0.0.1:8080/catalog.json")]
    [InlineData("http://10.0.0.1/catalog.json")]
    [InlineData("http://192.168.1.1/catalog.json")]
    [InlineData("http://172.16.0.1/catalog.json")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://myserver.local/catalog.json")]
    [InlineData("http://internal.service.internal/catalog.json")]
    public void IsSafeUrl_LoopbackOrPrivateUrl_ReturnsFalse(string url)
    {
        var result = NetworkSecurityHelper.IsSafeUrl(url, out var failureReason);

        Assert.False(result);
        Assert.NotNull(failureReason);
    }

    /// <summary>
    /// Verifies that null, empty, or non-HTTP(S) schemes are rejected.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.com/file.json")]
    [InlineData("file:///C:/catalog.json")]
    [InlineData("not-a-url")]
    public void IsSafeUrl_InvalidOrNonHttpUrl_ReturnsFalse(string? url)
    {
        var result = NetworkSecurityHelper.IsSafeUrl(url, out var failureReason);

        Assert.False(result);
        Assert.NotNull(failureReason);
    }

    /// <summary>
    /// Verifies IP safety checks.
    /// </summary>
    [Fact]
    public void IsSafeIpAddress_LoopbackAndPrivate_ReturnsFalse()
    {
        Assert.False(NetworkSecurityHelper.IsSafeIpAddress(IPAddress.Loopback));
        Assert.False(NetworkSecurityHelper.IsSafeIpAddress(IPAddress.IPv6Loopback));
        Assert.False(NetworkSecurityHelper.IsSafeIpAddress(IPAddress.Parse("10.0.0.5")));
        Assert.False(NetworkSecurityHelper.IsSafeIpAddress(IPAddress.Parse("192.168.0.1")));
        Assert.False(NetworkSecurityHelper.IsSafeIpAddress(IPAddress.Parse("172.20.1.1")));
        Assert.False(NetworkSecurityHelper.IsSafeIpAddress(null));
    }
}
