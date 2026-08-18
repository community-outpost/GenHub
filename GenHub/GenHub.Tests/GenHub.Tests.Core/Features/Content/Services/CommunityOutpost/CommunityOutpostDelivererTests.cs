using System.IO.Compression;
using System.Reflection;
using System.Text;
using GenHub.Core.Constants;
using GenHub.Features.Content.Services.CommunityOutpost;

namespace GenHub.Tests.Core.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Tests the containment and expansion bounds applied to Community Outpost archives, which arrive
/// from a third-party catalog and are therefore untrusted input.
/// </summary>
public sealed class CommunityOutpostDelivererTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "GenHubCommunityOutpost",
        Guid.NewGuid().ToString("N"));

    private readonly string _extractDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommunityOutpostDelivererTests"/> class.
    /// </summary>
    public CommunityOutpostDelivererTests()
    {
        _extractDirectory = Path.Combine(_workingDirectory, "extracted");
        Directory.CreateDirectory(_extractDirectory);
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
    /// Extracts entries that stay inside the target directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtractArchiveAsync_ExtractsEntriesWithinBudgetAsync()
    {
        var archivePath = Path.Combine(_workingDirectory, "content.zip");
        CreateArchive(archivePath, "patch/readme.txt", "generals.big");

        await InvokeExtractArchiveAsync(archivePath, _extractDirectory);

        Assert.True(File.Exists(Path.Combine(_extractDirectory, "patch", "readme.txt")));
        Assert.True(File.Exists(Path.Combine(_extractDirectory, "generals.big")));
    }

    /// <summary>
    /// Refuses an entry whose key climbs out of the extract directory, rather than depending on the
    /// archive library to block it.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtractArchiveAsync_RejectsEntryEscapingTheExtractDirectoryAsync()
    {
        var archivePath = Path.Combine(_workingDirectory, "traversal.zip");
        CreateArchive(archivePath, "../escaped.big");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeExtractArchiveAsync(archivePath, _extractDirectory));

        Assert.Contains("outside target directory", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_workingDirectory, "escaped.big")));
    }

    /// <summary>
    /// Refuses an archive that declares more entries than the extraction budget allows.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtractArchiveAsync_RejectsArchiveOverTheEntryBudgetAsync()
    {
        var archivePath = Path.Combine(_workingDirectory, "swarm.zip");
        var entryNames = Enumerable
            .Range(0, CommunityOutpostConstants.MaxArchiveEntries + 1)
            .Select(index => $"entry{index}.dat")
            .ToArray();
        CreateArchive(archivePath, entryNames);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeExtractArchiveAsync(archivePath, _extractDirectory));

        Assert.Contains("too many entries", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFileSystemEntries(_extractDirectory));
    }

    private static void CreateArchive(string archivePath, params string[] entryNames)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var entryName in entryNames)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(entryName));
        }
    }

    private static async Task InvokeExtractArchiveAsync(string archivePath, string extractPath)
    {
        var extract = typeof(CommunityOutpostDeliverer).GetMethod(
            "ExtractArchiveAsync",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CommunityOutpostDeliverer.ExtractArchiveAsync was not found.");

        await (Task)extract.Invoke(null, [archivePath, extractPath, CancellationToken.None])!;
    }
}
