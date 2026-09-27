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
    /// <param name="url">The URL to validate.</param>
    [Theory]
    [InlineData("https://raw.githubusercontent.com/user/repo/main/catalog.json")]
    [InlineData("https://example.com/mod/catalog.json")]
    [InlineData("https://example.com/catalog.json")]
    public void IsSafeUrl_ValidExternalUrl_ReturnsTrue(string url)
    {
        var result = NetworkSecurityHelper.IsSafeUrl(url, out var failureReason);

        Assert.True(result);
        Assert.Null(failureReason);
    }

    /// <summary>
    /// Verifies that plain HTTP URLs are rejected to match the HTTPS-only catalog policy.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    [Theory]
    [InlineData("http://example.com/catalog.json")]
    [InlineData("http://example.com/mod/catalog.json")]
    public void IsSafeUrl_PlainHttpUrl_ReturnsFalse(string url)
    {
        var result = NetworkSecurityHelper.IsSafeUrl(url, out var failureReason);

        Assert.False(result);
        Assert.NotNull(failureReason);
    }

    /// <summary>
    /// Verifies that loopback, local, and private URLs are blocked.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    [Theory]
    [InlineData("https://localhost/catalog.json")]
    [InlineData("https://app.localhost/catalog.json")]
    [InlineData("https://127.0.0.1/catalog.json")]
    [InlineData("https://127.0.0.1:8080/catalog.json")]
    [InlineData("https://10.0.0.1/catalog.json")]
    [InlineData("https://192.168.1.1/catalog.json")]
    [InlineData("https://172.16.0.1/catalog.json")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://myserver.local/catalog.json")]
    [InlineData("https://internal.service.internal/catalog.json")]
    [InlineData("http://localhost/catalog.json")]
    public void IsSafeUrl_LoopbackOrPrivateUrl_ReturnsFalse(string url)
    {
        var result = NetworkSecurityHelper.IsSafeUrl(url, out var failureReason);

        Assert.False(result);
        Assert.NotNull(failureReason);
    }

    /// <summary>
    /// Verifies that null, empty, or non-HTTPS schemes are rejected.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
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
    /// Verifies that host names failing DNS resolution are rejected instead of passing open.
    /// </summary>
    [Fact]
    public void IsSafeUrl_UnresolvableHost_ReturnsFalse()
    {
        var result = NetworkSecurityHelper.IsSafeUrl("https://unresolvable-host.invalid/catalog.json", out var failureReason);

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

    /// <summary>
    /// Verifies IPv6 transition embeddings of unsafe IPv4 addresses are blocked.
    /// </summary>
    /// <param name="address">The IPv6 address to validate.</param>
    [Theory]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::10.0.0.1")]
    [InlineData("2002:0a00:0001::")]
    [InlineData("2001::f5ff:fffe")]
    [InlineData("64:ff9b::0a00:0001")]
    public void IsSafeIpAddress_UnsafeEmbeddedIpv4_ReturnsFalse(string address)
    {
        Assert.False(NetworkSecurityHelper.IsSafeIpAddress(IPAddress.Parse(address)));
    }

    /// <summary>
    /// Verifies IPv6 transition embeddings of public IPv4 addresses pass validation.
    /// </summary>
    /// <param name="address">The IPv6 address to validate.</param>
    [Theory]
    [InlineData("::ffff:1.2.3.4")]
    [InlineData("::1.2.3.4")]
    [InlineData("2002:0102:0304::")]
    [InlineData("2001::fefd:fcfb")]
    [InlineData("64:ff9b::0102:0304")]
    public void IsSafeIpAddress_SafeEmbeddedIpv4_ReturnsTrue(string address)
    {
        Assert.True(NetworkSecurityHelper.IsSafeIpAddress(IPAddress.Parse(address)));
    }

    /// <summary>
    /// Verifies that deprecated IPv6 site-local unicast addresses (fec0::/10) are rejected.
    /// </summary>
    /// <param name="address">The IPv6 site-local address to validate.</param>
    [Theory]
    [InlineData("fec0::1")]
    [InlineData("fec0::")]
    [InlineData("feff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    [InlineData("fec0:0:0:ffff::1")]
    [InlineData("fed0::1")]
    [InlineData("fee0::1")]
    public void IsSafeIpAddress_Ipv6SiteLocal_ReturnsFalse(string address)
    {
        Assert.False(NetworkSecurityHelper.IsSafeIpAddress(IPAddress.Parse(address)));

        var urlResult = NetworkSecurityHelper.IsSafeUrl($"https://[{address}]/catalog.json", out var failureReason);
        Assert.False(urlResult);
        Assert.NotNull(failureReason);
    }
}
