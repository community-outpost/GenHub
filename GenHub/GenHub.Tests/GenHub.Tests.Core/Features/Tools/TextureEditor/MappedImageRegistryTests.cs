using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.TextureEditor;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.TextureEditor;

/// <summary>
/// Unit tests for <see cref="MappedImageRegistry"/>.
/// </summary>
public sealed class MappedImageRegistryTests
{
    private sealed class GatedParser(Task gate, string gatedDirectory, TaskCompletionSource entered) : ISageMappedImageParser
    {
        public OperationResult<IReadOnlyList<MappedImageDefinition>> ParseText(string content, string? sourcePath = null)
        {
            if (sourcePath != null && sourcePath.StartsWith(gatedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                entered.TrySetResult();
                gate.Wait(TimeSpan.FromSeconds(10));
                return OperationResult<IReadOnlyList<MappedImageDefinition>>.CreateSuccess(
                    [new MappedImageDefinition("a", "old.tga", 64, 64, 0, 0, 63, 63)],
                    TimeSpan.Zero);
            }

            return OperationResult<IReadOnlyList<MappedImageDefinition>>.CreateSuccess(
                [new MappedImageDefinition("Direct", "direct.tga", 64, 64, 0, 0, 63, 63)],
                TimeSpan.Zero);
        }

        public async Task<OperationResult<IReadOnlyList<MappedImageDefinition>>> ParseFileAsync(string path, CancellationToken cancellationToken = default)
        {
            if (path.StartsWith(gatedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                entered.TrySetResult();
                await gate.ConfigureAwait(false);
                return OperationResult<IReadOnlyList<MappedImageDefinition>>.CreateSuccess(
                    [new MappedImageDefinition("a", "old.tga", 64, 64, 0, 0, 63, 63)],
                    TimeSpan.Zero);
            }

            return OperationResult<IReadOnlyList<MappedImageDefinition>>.CreateSuccess(
                [new MappedImageDefinition("Direct", "direct.tga", 64, 64, 0, 0, 63, 63)],
                TimeSpan.Zero);
        }

        public string Serialize(IEnumerable<MappedImageDefinition> images, string? headerComment = null) =>
            throw new NotSupportedException();
    }

    private readonly MappedImageRegistry _registry = new(
        new SageMappedImageParser(NullLogger<SageMappedImageParser>.Instance),
        NullLogger<MappedImageRegistry>.Instance);

    /// <summary>
    /// Verifies that scanning indexes entries from nested INI files.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_NestedInis_IndexesAllEntriesAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(directory, "Nested"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "First.ini"), Block("Alpha", "a.tga"));
            await File.WriteAllTextAsync(Path.Combine(directory, "Nested", "Second.ini"), Block("Beta", "b.tga"));

            var result = await _registry.ScanDirectoryAsync(directory);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(2, result.Data.FilesScanned);
            Assert.Equal(2, _registry.Count);
            Assert.NotNull(_registry.GetByName("Alpha"));
            Assert.NotNull(_registry.GetByName("Beta"));
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that later alphabetical files override earlier entries like SAGE load order.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_DuplicateNames_LastAlphabeticalWinsAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "a_first.ini"), Block("Dupe", "early.tga"));
            await File.WriteAllTextAsync(Path.Combine(directory, "z_last.ini"), Block("Dupe", "late.tga"));

            var result = await _registry.ScanDirectoryAsync(directory);

            Assert.True(result.Success);
            Assert.Equal("late.tga", _registry.GetByName("Dupe")?.TextureFileName);
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that HandCreated overrides win over TextureSize files regardless of alphabetical order.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_HandCreatedOverride_WinsOverTextureSizeAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(directory, "TextureSize_512"));
        Directory.CreateDirectory(Path.Combine(directory, "HandCreated"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "TextureSize_512", "z_size.ini"), Block("Dupe", "size.tga"));
            await File.WriteAllTextAsync(Path.Combine(directory, "HandCreated", "a_hand.ini"), Block("Dupe", "hand.tga"));
            await File.WriteAllTextAsync(Path.Combine(directory, "z_base.ini"), Block("Dupe", "base.tga"));

            var result = await _registry.ScanDirectoryAsync(directory);

            Assert.True(result.Success);
            Assert.Equal("hand.tga", _registry.GetByName("Dupe")?.TextureFileName);
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that name lookups are case-insensitive.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetByName_MixedCase_FindsEntryAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "names.ini"), Block("SCC Ranger", "a.tga"));

            await _registry.ScanDirectoryAsync(directory);

            Assert.NotNull(_registry.GetByName("scc ranger"));
            Assert.NotNull(_registry.GetByName("SCC RANGER"));
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that texture lookups return matching entries ordered by name.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetByTexture_SharedTexture_ReturnsOrderedMatchesAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "shared.ini"), Block("Zebra", "shared.tga") + Block("Apple", "shared.tga") + Block("Other", "other.tga"));

            await _registry.ScanDirectoryAsync(directory);
            var matches = _registry.GetByTexture("SHARED.tga");

            Assert.Equal(2, matches.Count);
            Assert.Equal("Apple", matches[0].Name);
            Assert.Equal("Zebra", matches[1].Name);
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that scanning a missing directory returns a failure.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_MissingDirectory_ReturnsFailureAsync()
    {
        var result = await _registry.ScanDirectoryAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.True(result.Failed);
        Assert.Equal(0, _registry.Count);
    }

    /// <summary>
    /// Verifies that clear removes all indexed entries.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Clear_AfterScan_RemovesAllEntriesAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "one.ini"), Block("Solo", "a.tga"));
            await _registry.ScanDirectoryAsync(directory);
            Assert.Equal(1, _registry.Count);

            _registry.Clear();

            Assert.Equal(0, _registry.Count);
            Assert.Empty(_registry.All);
            Assert.Null(_registry.GetByName("Solo"));
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that overwritten duplicates count once in the scan result.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_DuplicateNames_ReportsDistinctCountAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "a_first.ini"), Block("Dupe", "early.tga"));
            await File.WriteAllTextAsync(Path.Combine(directory, "z_last.ini"), Block("Dupe", "late.tga"));

            var result = await _registry.ScanDirectoryAsync(directory);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(1, result.Data.ImagesIndexed);
            Assert.Equal(1, _registry.Count);
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that a second scan replaces earlier entries instead of accumulating them.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_SecondScan_ReplacesEarlierEntriesAsync()
    {
        string first = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string second = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(first, "one.ini"), Block("Alpha", "a.tga"));
            await File.WriteAllTextAsync(Path.Combine(second, "two.ini"), Block("Beta", "b.tga"));

            await _registry.ScanDirectoryAsync(first);
            var result = await _registry.ScanDirectoryAsync(second);

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(1, result.Data.ImagesIndexed);
            Assert.Null(_registry.GetByName("Alpha"));
            Assert.NotNull(_registry.GetByName("Beta"));
        }
        finally
        {
            DeleteDirectoryQuietly(first);
            DeleteDirectoryQuietly(second);
        }
    }

    /// <summary>
    /// Verifies that imported entries overwrite same-named entries.
    /// </summary>
    [Fact]
    public void ImportDefinitions_DuplicateName_OverwritesExisting()
    {
        _registry.ImportDefinitions([new GenHub.Core.Models.Tools.TextureEditor.MappedImageDefinition("Solo", "a.tga", 64, 64, 0, 0, 31, 31)]);
        _registry.ImportDefinitions([new GenHub.Core.Models.Tools.TextureEditor.MappedImageDefinition("Solo", "b.tga", 64, 64, 0, 0, 31, 31)]);

        Assert.Equal(1, _registry.Count);
        Assert.Equal("b.tga", _registry.GetByName("Solo")?.TextureFileName);
    }

    /// <summary>
    /// Verifies that a cancelled scan keeps the previously indexed entries.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_CancelledScan_PreservesPreviousEntriesAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "First.ini"), Block("Alpha", "a.tga"));

            var first = await _registry.ScanDirectoryAsync(directory);
            Assert.True(first.Success);
            Assert.Equal(1, _registry.Count);

            await File.WriteAllTextAsync(Path.Combine(directory, "Second.ini"), Block("Beta", "b.tga"));

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await _registry.ScanDirectoryAsync(directory, cancelled.Token));

            Assert.Equal(1, _registry.Count);
            Assert.NotNull(_registry.GetByName("Alpha"));
            Assert.Null(_registry.GetByName("Beta"));
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that a slower earlier scan does not overwrite a newer completed scan.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_SupersededScan_ThrowsAndKeepsNewerEntriesAsync()
    {
        string first = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string second = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(first, "a.ini"), Block("Old", "old.tga"));
            await File.WriteAllTextAsync(Path.Combine(second, "b.ini"), Block("New", "new.tga"));

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var parser = new GatedParser(gate.Task, first, entered);
            var registry = new MappedImageRegistry(parser, NullLogger<MappedImageRegistry>.Instance);

            var slow = registry.ScanDirectoryAsync(first);
            await entered.Task.ConfigureAwait(true);
            var fresh = await registry.ScanDirectoryAsync(second);
            Assert.True(fresh.Success);

            gate.SetResult();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await slow);

            Assert.Equal("direct.tga", registry.GetByName("Direct")?.TextureFileName);
            Assert.Null(registry.GetByName("a"));
        }
        finally
        {
            DeleteDirectoryQuietly(first);
            DeleteDirectoryQuietly(second);
        }
    }

    /// <summary>
    /// Verifies that clearing during a scan invalidates the stale scan instead of being repopulated.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_ClearDuringScan_InvalidatesStaleScanAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "a.ini"), Block("Old", "old.tga"));

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var parser = new GatedParser(gate.Task, directory, entered);
            var registry = new MappedImageRegistry(parser, NullLogger<MappedImageRegistry>.Instance);

            var slow = registry.ScanDirectoryAsync(directory);
            await entered.Task.ConfigureAwait(true);
            registry.Clear();
            gate.SetResult();

            await Assert.ThrowsAsync<OperationCanceledException>(async () => await slow);
            Assert.Equal(0, registry.Count);
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that non-MappedImages INI files (e.g. Scripts.ini) are skipped without parsing or warnings.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_NonMappedImageIniFiles_AreSkippedWithoutErrorsAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string scriptsDir = Path.Combine(directory, "Data", "Scripts");
        string mappedDir = Path.Combine(directory, "Data", "INI", "MappedImages");
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(mappedDir);

        try
        {
            string scriptsContent = string.Join(
                Environment.NewLine,
                "Script MyScript",
                "  Condition = Always",
                "  Action = DoNothing",
                "End");
            await File.WriteAllTextAsync(
                Path.Combine(scriptsDir, "Scripts.ini"),
                scriptsContent);

            await File.WriteAllTextAsync(
                Path.Combine(mappedDir, "Test.ini"),
                Block("ValidImage", "valid.tga"));

            var result = await _registry.ScanDirectoryAsync(directory);

            Assert.True(result.Success);
            Assert.Equal(1, _registry.Count);
            Assert.NotNull(_registry.GetByName("ValidImage"));
            Assert.Null(_registry.GetByName("MyScript"));
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that .BIG archives containing MappedImages are discovered and parsed.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_BigArchive_IndexesMappedImagesFromBigArchiveAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);

        try
        {
            string bigPath = Path.Combine(directory, "INIZH.big");
            string entryContent = Block("BigHero", "big_textures.tga");
            CreateTestBigArchive(bigPath, new Dictionary<string, byte[]>
            {
                [@"Data\INI\MappedImages\Heroes.ini"] = System.Text.Encoding.Latin1.GetBytes(entryContent),
            });

            var result = await _registry.ScanDirectoryAsync(directory);

            Assert.True(result.Success);
            Assert.Equal(1, _registry.Count);
            var hero = _registry.GetByName("BigHero");
            Assert.NotNull(hero);
            Assert.Equal("big_textures.tga", hero.TextureFileName);
            Assert.StartsWith(bigPath, hero.SourcePath, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(hero.SourcePath);
            Assert.Contains('#', hero.SourcePath);
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that .BIG archives with uppercase extension are discovered and parsed on case-sensitive file systems.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_UppercaseBigArchive_DiscoveredAndParsedAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);

        try
        {
            string bigPath = Path.Combine(directory, "TEXTURES.BIG");
            string entryContent = Block("UppercaseHero", "upper_textures.tga");
            CreateTestBigArchive(bigPath, new Dictionary<string, byte[]>
            {
                [@"Data\INI\MappedImages\Heroes.ini"] = System.Text.Encoding.Latin1.GetBytes(entryContent),
            });

            var result = await _registry.ScanDirectoryAsync(directory);

            Assert.True(result.Success);
            Assert.Equal(1, _registry.Count);
            var hero = _registry.GetByName("UppercaseHero");
            Assert.NotNull(hero);
            Assert.Equal("upper_textures.tga", hero.TextureFileName);
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that .BIG archives in nested subdirectories are discovered and parsed.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_NestedBigArchive_IndexesMappedImagesFromNestedBigArchiveAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string nestedDir = Path.Combine(directory, "SubFolder", "Archives");
        Directory.CreateDirectory(nestedDir);

        try
        {
            string bigPath = Path.Combine(nestedDir, "NestedINI.big");
            string entryContent = Block("NestedHero", "nested_textures.tga");
            CreateTestBigArchive(bigPath, new Dictionary<string, byte[]>
            {
                [@"Data\INI\MappedImages\Nested.ini"] = System.Text.Encoding.Latin1.GetBytes(entryContent),
            });

            var result = await _registry.ScanDirectoryAsync(directory);

            Assert.True(result.Success);
            Assert.Equal(1, _registry.Count);
            var hero = _registry.GetByName("NestedHero");
            Assert.NotNull(hero);
            Assert.Equal("nested_textures.tga", hero.TextureFileName);
            Assert.StartsWith(bigPath, hero.SourcePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    /// <summary>
    /// Verifies that loose files override entries from .BIG archives.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ScanDirectoryAsync_LooseFileOverridesBigArchiveAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string mappedDir = Path.Combine(directory, "Data", "INI", "MappedImages");
        Directory.CreateDirectory(mappedDir);

        try
        {
            string bigPath = Path.Combine(directory, "INI.big");
            CreateTestBigArchive(bigPath, new Dictionary<string, byte[]>
            {
                [@"Data\INI\MappedImages\Heroes.ini"] = System.Text.Encoding.Latin1.GetBytes(Block("SharedHero", "archive_tex.tga")),
            });

            await File.WriteAllTextAsync(
                Path.Combine(mappedDir, "Override.ini"),
                Block("SharedHero", "loose_tex.tga"));

            var result = await _registry.ScanDirectoryAsync(directory);

            Assert.True(result.Success);
            Assert.Equal(1, _registry.Count);
            var hero = _registry.GetByName("SharedHero");
            Assert.NotNull(hero);
            Assert.Equal("loose_tex.tga", hero.TextureFileName);
        }
        finally
        {
            DeleteDirectoryQuietly(directory);
        }
    }

    private static void DeleteDirectoryQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup in tests
        }
    }

    private static void CreateTestBigArchive(string filePath, Dictionary<string, byte[]> entries)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        int dirSize = 0;
        int totalPayload = 0;
        foreach (var (key, val) in entries)
        {
            dirSize += 8 + System.Text.Encoding.Latin1.GetByteCount(key) + 1;
            totalPayload += val.Length;
        }

        uint headerSize = (uint)(16 + dirSize);
        uint currentOffset = headerSize;
        uint totalFileSize = currentOffset + (uint)totalPayload;

        writer.Write(new byte[] { (byte)'B', (byte)'I', (byte)'G', (byte)'F' });
        writer.Write(totalFileSize);

        byte[] countBytes = BitConverter.GetBytes((uint)entries.Count);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(countBytes);
        }

        writer.Write(countBytes);

        byte[] headerSizeBytes = BitConverter.GetBytes(headerSize);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(headerSizeBytes);
        }

        writer.Write(headerSizeBytes);

        foreach (var (path, data) in entries)
        {
            byte[] offsetBytes = BitConverter.GetBytes(currentOffset);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(offsetBytes);
            }

            writer.Write(offsetBytes);

            byte[] sizeBytes = BitConverter.GetBytes((uint)data.Length);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(sizeBytes);
            }

            writer.Write(sizeBytes);

            writer.Write(System.Text.Encoding.Latin1.GetBytes(path));
            writer.Write((byte)0);

            currentOffset += (uint)data.Length;
        }

        foreach (var data in entries.Values)
        {
            writer.Write(data);
        }

        File.WriteAllBytes(filePath, ms.ToArray());
    }

    private static string Block(string name, string texture) =>
        $"MappedImage {name}\n  Texture = {texture}\n  TextureWidth = 64\n  TextureHeight = 64\n  Coords = Left:0 Top:0 Right:63 Bottom:63\n  Status = NONE\nEnd\n";
}
