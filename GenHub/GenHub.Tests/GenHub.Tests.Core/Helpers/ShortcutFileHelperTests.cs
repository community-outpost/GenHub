using GenHub.Core.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="ShortcutFileHelper"/>.
/// </summary>
public class ShortcutFileHelperTests
{
    /// <summary>
    /// Verifies that removing a missing shortcut returns success with false.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RemoveShortcutFileAsync_MissingFile_ReturnsFalseAsync()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"GenHubShortcutTest_{Guid.NewGuid():N}.lnk");

        var result = await ShortcutFileHelper.RemoveShortcutFileAsync(missingPath, "Profile", NullLogger.Instance);

        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Verifies that removing an existing shortcut deletes the file and returns success with true.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RemoveShortcutFileAsync_ExistingFile_DeletesAndReturnsTrueAsync()
    {
        var shortcutPath = Path.Combine(Path.GetTempPath(), $"GenHubShortcutTest_{Guid.NewGuid():N}.lnk");
        await File.WriteAllTextAsync(shortcutPath, "shortcut");

        try
        {
            var result = await ShortcutFileHelper.RemoveShortcutFileAsync(shortcutPath, "Profile", NullLogger.Instance);

            Assert.True(result.Success);
            Assert.True(result.Data);
            Assert.False(File.Exists(shortcutPath));
        }
        finally
        {
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
    }

    /// <summary>
    /// Verifies that a null shortcut path throws an argument null exception.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RemoveShortcutFileAsync_NullPath_ThrowsAsync()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ShortcutFileHelper.RemoveShortcutFileAsync(null!, "Profile", NullLogger.Instance));
    }
}
