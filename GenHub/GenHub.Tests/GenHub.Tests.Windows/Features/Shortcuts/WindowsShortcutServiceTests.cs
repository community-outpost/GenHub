using GenHub.Core.Models.GameProfile;
using GenHub.Windows.Features.Shortcuts;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Windows.Features.Shortcuts;

/// <summary>
/// Unit tests for <see cref="WindowsShortcutService"/>.
/// </summary>
public class WindowsShortcutServiceTests
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
        using var desktop = new TempDesktopDirectory();
        var service = new WindowsShortcutService(Mock.Of<ILogger<WindowsShortcutService>>(), () => desktop.Path);
        var profile = new GameProfile { Name = null! };

        // Act
        var result = await service.RemoveDesktopShortcutAsync(profile);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Verifies that removing a missing shortcut succeeds with false without touching the host desktop.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RemoveDesktopShortcutAsync_ReturnsFalseWhenShortcutMissingAsync()
    {
        // Arrange
        using var desktop = new TempDesktopDirectory();
        var service = new WindowsShortcutService(Mock.Of<ILogger<WindowsShortcutService>>(), () => desktop.Path);
        var profile = new GameProfile { Name = "GenHub Test Missing Shortcut 9f8a7b6c" };

        // Act
        var result = await service.RemoveDesktopShortcutAsync(profile);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Data);
        Assert.StartsWith(desktop.Path, service.GetShortcutPath(profile), StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that removing an existing shortcut deletes the file and reports success with true.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RemoveDesktopShortcutAsync_DeletesExistingShortcutAsync()
    {
        // Arrange
        using var desktop = new TempDesktopDirectory();
        var service = new WindowsShortcutService(Mock.Of<ILogger<WindowsShortcutService>>(), () => desktop.Path);
        var profile = new GameProfile { Name = "GenHub Test Existing Shortcut 9f8a7b6c" };
        var shortcutPath = service.GetShortcutPath(profile);
        await File.WriteAllTextAsync(shortcutPath, "shortcut");

        // Act
        var result = await service.RemoveDesktopShortcutAsync(profile);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Data);
        Assert.False(File.Exists(shortcutPath));
    }

    /// <summary>
    /// Scoped temporary desktop directory that is removed on dispose.
    /// </summary>
    private sealed class TempDesktopDirectory : IDisposable
    {
        public TempDesktopDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"GenHubShortcutTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
