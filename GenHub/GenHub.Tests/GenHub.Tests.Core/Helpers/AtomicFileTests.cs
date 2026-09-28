using GenHub.Core.Helpers;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="AtomicFile"/>.
/// </summary>
public sealed class AtomicFileTests
{
    /// <summary>
    /// Verifies that a byte write lands at the destination with exact content.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WriteAllBytesAsync_NewFile_WritesContentAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            await AtomicFile.WriteAllBytesAsync(path, [1, 2, 3, 4]);

            Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that a text write replaces an existing file atomically.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WriteAllTextAsync_ExistingFile_ReplacesContentAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini");
        try
        {
            await File.WriteAllTextAsync(path, "original");
            await AtomicFile.WriteAllTextAsync(path, "replaced");

            Assert.Equal("replaced", await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that cancellation preserves the previous destination content.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WriteAllBytesAsync_CancelledBeforeWrite_PreservesDestinationAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            await File.WriteAllBytesAsync(path, [9, 9]);
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            await Assert.ThrowsAsync<OperationCanceledException>(() => AtomicFile.WriteAllBytesAsync(path, [1, 2], cancelled.Token));

            Assert.Equal([9, 9], await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that text writes use UTF-8 without a byte order mark.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WriteAllTextAsync_NewFile_WritesUtf8WithoutBomAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ini");
        try
        {
            await AtomicFile.WriteAllTextAsync(path, "MappedImage");

            byte[] bytes = await File.ReadAllBytesAsync(path);
            Assert.Equal("MappedImage", Encoding.UTF8.GetString(bytes));
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
