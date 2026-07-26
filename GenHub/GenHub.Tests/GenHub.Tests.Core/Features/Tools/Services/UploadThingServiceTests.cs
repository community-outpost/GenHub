using GenHub.Features.Tools.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Tests for the disabled UploadThing integration.
/// </summary>
public class UploadThingServiceTests
{
    private readonly UploadThingService _service =
        new(Mock.Of<ILogger<UploadThingService>>());

    /// <summary>
    /// Verifies that uploads fail closed while short-lived credentials are unavailable.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task UploadFileAsync_WhenCredentialsAreUnavailable_ReturnsNull()
    {
        var result = await _service.UploadFileAsync("unused.zip");

        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that authenticated deletion also fails closed.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeleteFileAsync_WhenCredentialsAreUnavailable_ReturnsFalse()
    {
        var result = await _service.DeleteFileAsync("unused-key");

        Assert.False(result);
    }
}
