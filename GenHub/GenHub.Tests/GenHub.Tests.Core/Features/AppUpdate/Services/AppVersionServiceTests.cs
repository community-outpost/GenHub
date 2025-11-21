using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Contains unit tests for <see cref="AppVersionService"/>.
/// </summary>
public class AppVersionServiceTests
{
    private readonly AppVersionService _service = new(Mock.Of<ILogger<AppVersionService>>());

    /// <summary>
    /// Tests that <see cref="AppVersionService.GetCurrentVersion"/> returns a non-empty string.
    /// </summary>
    [Fact]
    public void GetCurrentVersion_ReturnsNonEmptyString()
    {
        // Act
        var result = _service.GetCurrentVersion();

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    /// <summary>
    /// Tests that GetCurrentVersionObject returns a valid Version object.
    /// </summary>
    [Fact]
    public void GetCurrentVersionObject_ReturnsValidVersion()
    {
        // Act
        var result = _service.GetCurrentVersionObject();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Major >= 0);
        Assert.True(result.Minor >= 0);
    }
}