using GenHub.Core.Models.GameProfile;
using GenHub.Linux.Features.Shortcuts;
using Microsoft.Extensions.Logging;
using Moq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Linux.Features.Shortcuts;

/// <summary>
/// Unit tests for <see cref="LinuxShortcutService"/>.
/// </summary>
[SupportedOSPlatform("linux")]
public class LinuxShortcutServiceTests
{
    /// <summary>
    /// Verifies that removing a shortcut never throws for a null profile name because
    /// filename sanitization is null-tolerant; a missing shortcut reports success with false.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RemoveDesktopShortcutAsync_DoesNotThrowWhenProfileNameIsNullAsync()
    {
        // Arrange
        var service = new LinuxShortcutService(Mock.Of<ILogger<LinuxShortcutService>>());
        var profile = new GameProfile { Name = null! };

        // Act
        var result = await service.RemoveDesktopShortcutAsync(profile);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Verifies that removing a missing shortcut succeeds with false.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RemoveDesktopShortcutAsync_ReturnsFalseWhenShortcutMissingAsync()
    {
        // Arrange
        var service = new LinuxShortcutService(Mock.Of<ILogger<LinuxShortcutService>>());
        var profile = new GameProfile { Name = "GenHub Test Missing Shortcut 9f8a7b6c" };

        // Act
        var result = await service.RemoveDesktopShortcutAsync(profile);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data);
    }
}
