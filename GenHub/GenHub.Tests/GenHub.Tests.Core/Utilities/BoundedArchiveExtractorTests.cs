using System.IO.Compression;
using System.Text;
using GenHub.Core.Constants;
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
    /// Leaves the existing destination untouched when an overwriting copy fails part-way through.
    /// The replacement is staged beside the destination, so the only file removed is the one this
    /// call wrote.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_KeepsExistingFileWhenOverwritingCopyFailsAsync()
    {
        var destination = Path.Combine(_workingDirectory, "replaced.dat");
        await File.WriteAllTextAsync(destination, "original");
        using var source = new MemoryStream(new byte[64 * 1024]);

        await Assert.ThrowsAsync<ArchiveExpansionLimitExceededException>(() =>
            BoundedArchiveExtractor.CopyEntryToFileAsync(
                source,
                destination,
                "replaced.dat",
                maxEntryBytes: 1024,
                remainingAggregateBytes: long.MaxValue,
                overwrite: true));

        Assert.Equal("original", await File.ReadAllTextAsync(destination));
        Assert.Equal([destination], Directory.GetFiles(_workingDirectory));
    }

    /// <summary>
    /// Replaces the existing destination once an overwriting copy completes, leaving no staging
    /// file behind.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_ReplacesExistingFileWhenOverwriteAllowedAsync()
    {
        var destination = Path.Combine(_workingDirectory, "replaced.dat");
        await File.WriteAllTextAsync(destination, "original");
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("replacement"));

        var written = await BoundedArchiveExtractor.CopyEntryToFileAsync(
            source,
            destination,
            "replaced.dat",
            maxEntryBytes: 1024,
            remainingAggregateBytes: 1024,
            overwrite: true);

        Assert.Equal("replacement".Length, written);
        Assert.Equal("replacement", await File.ReadAllTextAsync(destination));
        Assert.Equal([destination], Directory.GetFiles(_workingDirectory));
    }

    /// <summary>
    /// Rejects an entry once the archive-wide budget is spent, even when the entry is empty and so
    /// never reaches the read loop where the running total is checked.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_RejectsEmptyEntryOnceAggregateBudgetIsSpentAsync()
    {
        using var source = new MemoryStream([]);
        var destination = Path.Combine(_workingDirectory, "empty.dat");

        var failure = await Assert.ThrowsAsync<ArchiveExpansionLimitExceededException>(() =>
            BoundedArchiveExtractor.CopyEntryToFileAsync(
                source,
                destination,
                "empty.dat",
                maxEntryBytes: 1024,
                remainingAggregateBytes: 0));

        Assert.Equal("empty.dat", failure.EntryName);
        Assert.False(File.Exists(destination));
    }

    /// <summary>
    /// Shrinks the archive-wide budget across the entries of one archive the way its callers do, so
    /// an entry that fits its own cap comfortably is still refused once earlier entries have spent
    /// what the archive was allowed. Only the surviving entries are left on disk.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_ShrinksTheAggregateBudgetAcrossEntriesAsync()
    {
        const long aggregateBudget = 4096;
        const long entryCap = 4096;
        int[] entrySizes = [3000, 1000, 200];
        long expandedBytes = 0;

        for (var index = 0; index < entrySizes.Length - 1; index++)
        {
            using var source = new MemoryStream(new byte[entrySizes[index]]);
            expandedBytes += await BoundedArchiveExtractor.CopyEntryToFileAsync(
                source,
                Path.Combine(_workingDirectory, $"entry{index}.dat"),
                $"entry{index}.dat",
                entryCap,
                aggregateBudget - expandedBytes);
        }

        Assert.Equal(4000, expandedBytes);

        using var lastSource = new MemoryStream(new byte[entrySizes[^1]]);
        var lastDestination = Path.Combine(_workingDirectory, "entry2.dat");

        var failure = await Assert.ThrowsAsync<ArchiveExpansionLimitExceededException>(() =>
            BoundedArchiveExtractor.CopyEntryToFileAsync(
                lastSource,
                lastDestination,
                "entry2.dat",
                entryCap,
                aggregateBudget - expandedBytes));

        Assert.Equal(aggregateBudget - expandedBytes, failure.LimitBytes);
        Assert.False(File.Exists(lastDestination));
        Assert.Equal(2, Directory.GetFiles(_workingDirectory).Length);
    }

    /// <summary>
    /// Names the exhausted budget rather than the entry when the archive had nothing left to spend,
    /// so a diagnostic does not report an entry as expanding past a limit of zero bytes.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_ReportsASpentBudgetSeparatelyFromAnOversizedEntryAsync()
    {
        using var spent = new MemoryStream(new byte[16]);
        using var oversized = new MemoryStream(new byte[64 * 1024]);

        var spentFailure = await Assert.ThrowsAsync<ArchiveExpansionLimitExceededException>(() =>
            BoundedArchiveExtractor.CopyEntryToFileAsync(
                spent,
                Path.Combine(_workingDirectory, "spent.dat"),
                "spent.dat",
                maxEntryBytes: 1024,
                remainingAggregateBytes: 0));

        var oversizedFailure = await Assert.ThrowsAsync<ArchiveExpansionLimitExceededException>(() =>
            BoundedArchiveExtractor.CopyEntryToFileAsync(
                oversized,
                Path.Combine(_workingDirectory, "oversized.dat"),
                "oversized.dat",
                maxEntryBytes: 1024,
                remainingAggregateBytes: long.MaxValue));

        Assert.Contains("budget was already spent", spentFailure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("expanded past", spentFailure.Message, StringComparison.Ordinal);
        Assert.Contains("expanded past the allowed 1024 bytes", oversizedFailure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stages an overwriting write under a name of its own rather than one built from the
    /// destination, so a destination close to the Windows path limit is not pushed past it by the
    /// staging name alone.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CopyEntryToFileAsync_StagesUnderANameThatDoesNotGrowWithTheDestinationAsync()
    {
        var destination = Path.Combine(_workingDirectory, new string('n', 120) + ".dat");
        await File.WriteAllTextAsync(destination, "original");
        using var source = new DirectoryObservingStream(_workingDirectory, 64 * 1024);

        await Assert.ThrowsAsync<ArchiveExpansionLimitExceededException>(() =>
            BoundedArchiveExtractor.CopyEntryToFileAsync(
                source,
                destination,
                "long.dat",
                maxEntryBytes: 1024,
                remainingAggregateBytes: long.MaxValue,
                overwrite: true));

        var staged = Assert.Single(source.ObservedFiles.Where(file => file != destination).Distinct());
        Assert.EndsWith(IoConstants.StagingFileSuffix, staged, StringComparison.Ordinal);
        Assert.True(
            staged.Length < destination.Length,
            $"the staging path '{staged}' is longer than the destination it replaces");
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

    private sealed class DirectoryObservingStream(string directory, int length)
        : MemoryStream(new byte[length])
    {
        public List<string> ObservedFiles { get; } = [];

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObservedFiles.AddRange(Directory.GetFiles(directory));

            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
