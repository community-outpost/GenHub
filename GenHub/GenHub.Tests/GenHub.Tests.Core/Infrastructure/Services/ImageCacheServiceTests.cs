using GenHub.Infrastructure.Services;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure.Services;

/// <summary>
/// Unit tests for <see cref="ImageCacheService"/> SSRF protections.
/// </summary>
public sealed class ImageCacheServiceTests
{
    /// <summary>
    /// Verifies that the SSRF-safe handler disables automatic redirects so callers
    /// like the remote file size probe observe and validate every redirect hop.
    /// </summary>
    [Fact]
    public void CreateSsrfSafeSocketsHttpHandler_DisablesAutoRedirect()
    {
        using var handler = ImageCacheService.CreateSsrfSafeSocketsHttpHandler();

        Assert.False(handler.AllowAutoRedirect);
    }

    /// <summary>
    /// Verifies that the SSRF-safe handler filters resolved addresses at connect time.
    /// </summary>
    [Fact]
    public void CreateSsrfSafeSocketsHttpHandler_ConfiguresConnectCallback()
    {
        using var handler = ImageCacheService.CreateSsrfSafeSocketsHttpHandler();

        Assert.NotNull(handler.ConnectCallback);
    }
}
