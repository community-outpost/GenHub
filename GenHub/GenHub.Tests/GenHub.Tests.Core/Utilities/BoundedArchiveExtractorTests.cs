using System.IO.Compression;
using System.Text;
using GenHub.Core.Exceptions;
using GenHub.Core.Utilities;
using GenHub.Tests.Core.Infrastructure;
using SharpCompress.Archives;

namespace GenHub.Tests.Core.Utilities;

/// <summary>
/// Tests that archive entries are bounded by the bytes they actually expand to.
/// </summary>
public sealed class BoundedArchiveExtractorTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "GenHubBoundedExtractor",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="BoundedArchiveExtractorTests"/> class.
    /// </summary>
    public BoundedArchiveExtractorTests()
    {
        Directory.CreateDirectory(_workingDirectory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Writes the whole entry and reports the byte count when it fits inside both budgets.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_WritesEntryWithinBudgetAsync()
    {
        var payload = Encoding.UTF8.GetBytes("map contents");
        using var source = new MemoryStream(payload);
        var destination = Path.Combine(_workingDirectory, "entry.dat");

        var written = await BoundedArchiveExtractor.CopyEntryToFileAsync(
            source,
            destination,
            "entry.dat",
            maxEntryBytes: 1024,
            remainingAggregateBytes: 1024);

        Assert.Equal(payload.Length, written);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    /// <summary>
    /// Aborts and removes the partial output when an entry expands past its own cap.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_RejectsEntryOverPerEntryCapAndDeletesPartialOutputAsync()
    {
        using var source = new MemoryStream(new byte[64 * 1024]);
        var destination = Path.Combine(_workingDirectory, "bomb.dat");

        var failure = await Assert.ThrowsAsync<ArchiveExpansionLimitExceededException>(() =>
            BoundedArchiveExtractor.CopyEntryToFileAsync(
                source,
                destination,
                "bomb.dat",
                maxEntryBytes: 1024,
                remainingAggregateBytes: long.MaxValue));

        Assert.Equal("bomb.dat", failure.EntryName);
        Assert.Equal(1024, failure.LimitBytes);
        Assert.False(File.Exists(destination));
    }

    /// <summary>
    /// Aborts when an entry fits its own cap but exhausts what remains of the archive-wide budget.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_RejectsEntryOverRemainingAggregateBudgetAsync()
    {
        using var source = new MemoryStream(new byte[64 * 1024]);
        var destination = Path.Combine(_workingDirectory, "aggregate.dat");

        var failure = await Assert.ThrowsAsync<ArchiveExpansionLimitExceededException>(() =>
            BoundedArchiveExtractor.CopyEntryToFileAsync(
                source,
                destination,
                "aggregate.dat",
                maxEntryBytes: long.MaxValue,
                remainingAggregateBytes: 2048));

        Assert.Equal(2048, failure.LimitBytes);
        Assert.False(File.Exists(destination));
    }

    /// <summary>
    /// Leaves an existing destination untouched when overwriting is not permitted.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_KeepsExistingFileWhenOverwriteNotAllowedAsync()
    {
        var destination = Path.Combine(_workingDirectory, "existing.dat");
        await File.WriteAllTextAsync(destination, "original");
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("replacement"));

        await Assert.ThrowsAsync<IOException>(() =>
            BoundedArchiveExtractor.CopyEntryToFileAsync(
                source,
                destination,
                "existing.dat",
                maxEntryBytes: 1024,
                remainingAggregateBytes: 1024));

        Assert.Equal("original", await File.ReadAllTextAsync(destination));
    }

    /// <summary>
    /// Rejects an archive entry whose central-directory header understates its real size. The
    /// archive claims four kilobytes and inflates to twelve megabytes, which is only visible while
    /// decompressing, so the copy must abort mid-stream and leave no partial output behind.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_RejectsArchiveThatUnderstatesItsDeclaredSizeAsync()
    {
        const int actualBytes = 12 * 1024 * 1024;
        const int declaredBytes = 4096;
        const long entryCap = 1024 * 1024;

        var archivePath = Path.Combine(_workingDirectory, "spoofed.zip");
        ArchiveFixtures.CreateWithSpoofedEntrySize(archivePath, "bomb.dat", actualBytes, declaredBytes);

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var entry = archive.Entries.First(e => !e.IsDirectory);
        Assert.Equal(declaredBytes, entry.Size);

        var destination = Path.Combine(_workingDirectory, "bomb.extracted");
        await using var entryStream = entry.OpenEntryStream();

        var failure = await Assert.ThrowsAsync<ArchiveExpansionLimitExceededException>(() =>
            BoundedArchiveExtractor.CopyEntryToFileAsync(
                entryStream,
                destination,
                entry.Key ?? string.Empty,
                maxEntryBytes: entryCap,
                remainingAggregateBytes: long.MaxValue));

        Assert.Equal(entryCap, failure.LimitBytes);
        Assert.False(File.Exists(destination));
    }
}
