using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for multi-file staging, archive previews, and accent presets
/// in <see cref="AddContentDialogViewModel"/>.
/// </summary>
public sealed class PublisherStudioDialogStagingTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "GenHubStagingTests_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Staging two files must create one artifact per file and mark the initial
    /// release bundled so players install both files as one package.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PopulateFromPaths_TwoFiles_BundlesArtifactsOnCreateAsync()
    {
        var first = WriteTempFile("controlbar-a.big", "first-payload");
        var second = WriteTempFile("controlbar-b.big", "second-payload");
        CatalogContentItem? created = null;
        var vm = new AddContentDialogViewModel(item => created = item);

        vm.PopulateFromPaths([first, second]);

        Assert.Equal(2, vm.StagedFiles.Count);
        Assert.True(vm.IsMultiFileStaging);
        Assert.NotNull(vm.MultiFileBundleNote);
        Assert.Equal(first, vm.LocalFilePath);

        await WaitForComputeAsync(vm);

        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(created);
        var release = Assert.Single(created.Releases);
        Assert.True(release.BundleArtifacts);
        Assert.Equal(2, release.Artifacts.Count);
        Assert.Equal("controlbar-a.big", release.Artifacts[0].Filename);
        Assert.Equal("controlbar-b.big", release.Artifacts[1].Filename);
        Assert.True(release.Artifacts[0].IsPrimary);
        Assert.False(release.Artifacts[1].IsPrimary);
        Assert.All(release.Artifacts, a => Assert.True(a.Size > 0));
    }

    /// <summary>
    /// A single staged file keeps the legacy one-artifact path without bundling.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PopulateFromPath_SingleFile_CreatesSingleUnbundledArtifactAsync()
    {
        var file = WriteTempFile("single-mod.big", "payload");
        CatalogContentItem? created = null;
        var vm = new AddContentDialogViewModel(item => created = item);

        vm.PopulateFromPath(file);
        await WaitForComputeAsync(vm);

        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(created);
        var release = Assert.Single(created.Releases);
        Assert.False(release.BundleArtifacts);
        var artifact = Assert.Single(release.Artifacts);
        Assert.Equal("single-mod.big", artifact.Filename);
        Assert.True(artifact.IsPrimary);
    }

    /// <summary>
    /// Staging a zip archive must surface its entry count and the automatic
    /// extraction note so publishers know installs unpack it.
    /// </summary>
    [Fact]
    public void PopulateFromPath_ZipFile_ShowsArchiveNote()
    {
        var zipPath = Path.Combine(TempDir(), "bundle.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("a.big");
            archive.CreateEntry("b.big");
        }

        var vm = new AddContentDialogViewModel(_ => { });
        vm.PopulateFromPath(zipPath);

        var entry = Assert.Single(vm.StagedFiles);
        Assert.True(entry.IsArchive);
        Assert.NotNull(entry.ArchiveNoteText);
        Assert.Contains("2", entry.ArchiveNoteText);
    }

    /// <summary>
    /// Removing a staged file must drop its entry and re-sync the primary path.
    /// </summary>
    [Fact]
    public void RemoveStagedFile_RemovesEntryAndSyncsPrimary()
    {
        var first = WriteTempFile("one.big", "one");
        var second = WriteTempFile("two.big", "two");
        var vm = new AddContentDialogViewModel(_ => { });
        vm.PopulateFromPaths([first, second]);

        vm.RemoveStagedFileCommand.Execute(vm.StagedFiles[0]);

        var remaining = Assert.Single(vm.StagedFiles);
        Assert.Equal(second, remaining.LocalPath);
        Assert.Equal(second, vm.LocalFilePath);
        Assert.False(vm.IsMultiFileStaging);
    }

    /// <summary>
    /// Switching to direct-URL mode must clear staged files and primary metadata.
    /// </summary>
    [Fact]
    public void ToggleToDirectUrl_ClearsStagedFiles()
    {
        var file = WriteTempFile("mod.big", "payload");
        var vm = new AddContentDialogViewModel(_ => { });
        vm.PopulateFromPath(file);
        Assert.NotEmpty(vm.StagedFiles);

        vm.UseDirectUrl = true;

        Assert.Empty(vm.StagedFiles);
        Assert.False(vm.HasStagedFiles);
        Assert.Null(vm.LocalFilePath);
    }

    /// <summary>
    /// Picking a preset accent color must apply its hex value.
    /// </summary>
    [Fact]
    public void SelectAccentColor_SetsAccentColor()
    {
        var vm = new AddContentDialogViewModel(_ => { });

        vm.SelectAccentColorCommand.Execute("#DC2626");

        Assert.Equal("#DC2626", vm.AccentColor);
    }

    /// <summary>
    /// Verifies that clearing artwork fields in edit mode does not resurrect previous metadata.
    /// </summary>
    [Fact]
    public void EditMode_ClearingArtwork_DoesNotResurrectOldMetadata()
    {
        var existingItem = new CatalogContentItem
        {
            Id = "mod-test",
            Name = "Test Mod",
            Description = "Initial description that meets length requirements",
            ContentType = ContentType.Mod,
            TargetGame = GameType.Generals,
            Metadata = new ContentRichMetadata
            {
                IconUrl = "https://example.com/old_icon.png",
                BannerUrl = "https://example.com/old_banner.png",
            },
        };

        CatalogContentItem? savedItem = null;
        var vm = new AddContentDialogViewModel(existingItem, item => savedItem = item);

        // Clear the icon in edit mode
        vm.IconArtwork = string.Empty;

        // Save
        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(savedItem);
        Assert.Null(savedItem.Metadata?.IconUrl);
        Assert.Equal("https://example.com/old_banner.png", savedItem.Metadata?.BannerUrl);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string TempDir()
    {
        Directory.CreateDirectory(_tempDir);
        return _tempDir;
    }

    private string WriteTempFile(string fileName, string content)
    {
        var path = Path.Combine(TempDir(), fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task WaitForComputeAsync(AddContentDialogViewModel vm)
    {
        var timeout = TimeSpan.FromSeconds(5);
        var start = DateTime.UtcNow;
        while ((vm.IsComputingHash || string.IsNullOrEmpty(vm.Sha256Hash)) && DateTime.UtcNow - start < timeout)
        {
            await Task.Delay(50);
        }
    }
}
