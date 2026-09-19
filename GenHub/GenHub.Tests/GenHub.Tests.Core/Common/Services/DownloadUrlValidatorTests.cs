using GenHub.Common.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Unit tests for <see cref="DownloadUrlValidator"/>.
/// </summary>
public sealed class DownloadUrlValidatorTests
{
    private readonly DownloadUrlValidator _validator = new();

    /// <summary>
    /// Verifies that literal IP targets are accepted only for public addresses.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <param name="expected">The expected validation result.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("http://8.8.8.8/maps.zip", true)]
    [InlineData("https://1.1.1.1/replay.rep", true)]
    [InlineData("https://[2001:4860:4860::8888]/maps.zip", true)]
    [InlineData("http://127.0.0.1/maps.zip", false)]
    [InlineData("http://10.0.0.5/maps.zip", false)]
    [InlineData("http://192.168.1.1/maps.zip", false)]
    [InlineData("http://172.16.5.4/maps.zip", false)]
    [InlineData("http://169.254.169.254/latest/meta-data/", false)]
    [InlineData("http://0.0.0.0/maps.zip", false)]
    [InlineData("http://224.0.0.1/maps.zip", false)]
    [InlineData("http://[::1]/maps.zip", false)]
    [InlineData("http://[::ffff:127.0.0.1]/maps.zip", false)]
    public async Task IsSafeAsync_WithLiteralIp_ReturnsExpectedResultAsync(string url, bool expected)
    {
        var result = await _validator.IsSafeAsync(new Uri(url), CancellationToken.None);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that non-HTTP(S) schemes are rejected without DNS resolution.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("ftp://8.8.8.8/maps.zip")]
    [InlineData("file:///etc/passwd")]
    [InlineData("genhub://map/import?url=https%3A%2F%2Fexample.com%2Fmaps.zip")]
    public async Task IsSafeAsync_WithNonHttpScheme_ReturnsFalseAsync(string url)
    {
        var result = await _validator.IsSafeAsync(new Uri(url), CancellationToken.None);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that host names resolving to loopback are rejected.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IsSafeAsync_WithLocalhost_ReturnsFalseAsync()
    {
        var result = await _validator.IsSafeAsync(new Uri("http://localhost/maps.zip"), CancellationToken.None);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that unresolvable hosts fail closed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task IsSafeAsync_WithUnresolvableHost_ReturnsFalseAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var result = await _validator.IsSafeAsync(new Uri("https://nonexistent-host.invalid/maps.zip"), timeout.Token);

        Assert.False(result);
    }
}
