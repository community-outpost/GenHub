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
        public OperationResult<IReadOnlyList<MappedImageDefinition>> ParseText(string content, string? sourcePath = null) =>
            throw new NotSupportedException();

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
            Directory.Delete(directory, true);
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
            Directory.Delete(directory, true);
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
            Directory.Delete(directory, true);
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
            Directory.Delete(directory, true);
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
            Directory.Delete(directory, true);
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
            Directory.Delete(directory, true);
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
            Directory.Delete(directory, true);
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
            Directory.Delete(first, true);
            Directory.Delete(second, true);
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
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await _registry.ScanDirectoryAsync(directory, cancelled.Token));

            Assert.Equal(1, _registry.Count);
            Assert.NotNull(_registry.GetByName("Alpha"));
            Assert.Null(_registry.GetByName("Beta"));
        }
        finally
        {
            Directory.Delete(directory, true);
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
            Directory.Delete(first, true);
            Directory.Delete(second, true);
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
            Directory.Delete(directory, true);
        }
    }

    private static string Block(string name, string texture) =>
        $"MappedImage {name}\n  Texture = {texture}\n  TextureWidth = 64\n  TextureHeight = 64\n  Coords = Left:0 Top:0 Right:63 Bottom:63\n  Status = NONE\nEnd\n";
}
