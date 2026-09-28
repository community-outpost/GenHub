using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GenHub.Common.Editors;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Features.Tools.TextureEditor.Services;
using GenHub.Features.Tools.TextureEditor.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.TextureEditor;

/// <summary>
/// Unit tests for <see cref="TextureEditorViewModel"/> slice presets, registry loading, and save guards.
/// </summary>
public sealed class TextureEditorViewModelTests
{
    private static readonly byte[] ValidPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>
    /// Verifies that size presets resize the selected slice from its origin.
    /// </summary>
    /// <param name="preset">The preset key.</param>
    /// <param name="width">The expected width.</param>
    /// <param name="height">The expected height.</param>
    [Theory]
    [InlineData("64x64", 64, 64)]
    [InlineData("60x48", 60, 48)]
    [InlineData("32x32", 32, 32)]
    [InlineData("128x128", 128, 128)]
    [InlineData("256x256", 256, 256)]
    public void ApplyPreset_SizeKeys_ResizeSliceFromOrigin(string preset, int width, int height)
    {
        var viewModel = CreateViewModel();
        var slice = new TextureSliceViewModel(new MappedImageDefinition("Solo", "a.tga", 512, 512, 10, 20, 30, 40));
        viewModel.Slices.Add(slice);
        viewModel.SelectedSlice = slice;

        viewModel.ApplyPresetCommand.Execute(preset);

        // SAGE edges are exclusive, matching WndMappedImage: Width = Right - Left.
        Assert.Equal(10 + width, slice.Right);
        Assert.Equal(20 + height, slice.Bottom);
        Assert.Equal(width, slice.Width);
        Assert.Equal(height, slice.Height);
    }

    /// <summary>
    /// Verifies that disposing twice is safe and runs cleanup once.
    /// </summary>
    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var viewModel = CreateViewModel();

        viewModel.Dispose();
        var exception = Record.Exception(() => viewModel.Dispose());

        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies that loading a registry entry without an open atlas informs and is skipped.
    /// </summary>
    [Fact]
    public void LoadRegistryEntry_NoAtlas_ShowsInfoAndSkips()
    {
        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(notifications: notifications);

        viewModel.LoadRegistryEntry(new MappedImageDefinition("Local", "atlas.tga", 1, 1, 0, 0, 1, 1));

        Assert.Empty(viewModel.Slices);
        notifications.Verify(
            notification => notification.ShowInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a registry entry from another texture warns and is not loaded.
    /// </summary>
    [AvaloniaFact]
    public void LoadRegistryEntry_TextureMismatch_WarnsAndSkips()
    {
        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(notifications: notifications);
        using var bitmap = OpenAtlas(viewModel, "atlas.tga");

        viewModel.LoadRegistryEntry(new MappedImageDefinition("Foreign", "other.tga", 64, 64, 0, 0, 32, 32));

        Assert.Empty(viewModel.Slices);
        notifications.Verify(
            notification => notification.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a registry entry from the open atlas is added as a slice.
    /// </summary>
    [AvaloniaFact]
    public void LoadRegistryEntry_MatchingTexture_AddsSlice()
    {
        var viewModel = CreateViewModel();
        using var bitmap = OpenAtlas(viewModel, "atlas.tga");

        viewModel.LoadRegistryEntry(new MappedImageDefinition("Local", "atlas.tga", 1, 1, 0, 0, 1, 1));

        Assert.Single(viewModel.Slices);
        Assert.Same(viewModel.Slices[0], viewModel.SelectedSlice);
    }

    /// <summary>
    /// Verifies that a registry entry from another directory warns and is not loaded,
    /// even when the texture name matches.
    /// </summary>
    [AvaloniaFact]
    public void LoadRegistryEntry_ForeignDirectory_WarnsAndSkips()
    {
        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(notifications: notifications);
        using var bitmap = OpenAtlas(viewModel, "atlas.tga");
        string foreignIni = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "icons.ini");

        viewModel.LoadRegistryEntry(new MappedImageDefinition("Stale", "atlas.tga", 1, 1, 0, 0, 1, 1, SourcePath: foreignIni));

        Assert.Empty(viewModel.Slices);
        notifications.Verify(
            notification => notification.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a registry entry from the atlas directory is adopted.
    /// </summary>
    [AvaloniaFact]
    public void LoadRegistryEntry_SameDirectory_AddsSlice()
    {
        var viewModel = CreateViewModel();
        using var bitmap = OpenAtlas(viewModel, "atlas.tga");
        string siblingIni = Path.Combine(Path.GetTempPath(), "icons.ini");

        viewModel.LoadRegistryEntry(new MappedImageDefinition("Local", "atlas.tga", 1, 1, 0, 0, 1, 1, SourcePath: siblingIni));

        Assert.Single(viewModel.Slices);
    }

    /// <summary>
    /// Verifies that saving zero slices over a non-empty INI asks for confirmation first.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task SaveCommand_EmptySlicesOverNonEmptyIni_ConfirmsFirstAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            string iniPath = Path.Combine(directory, "atlas.ini");
            await File.WriteAllTextAsync(iniPath, "old content");
            var dialogs = new Mock<IDialogService>();
            dialogs.Setup(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(false);
            var viewModel = CreateViewModel(dialogs: dialogs);
            viewModel.AtlasPath = Path.Combine(directory, "atlas.tga");
            using var stream = new MemoryStream(ValidPngBytes);
            using var bitmap = new Bitmap(stream);
            viewModel.AtlasBitmap = bitmap;

            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.Equal("old content", await File.ReadAllTextAsync(iniPath));
            dialogs.Verify(
                dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that saving zero slices over a missing INI writes without confirmation.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task SaveCommand_EmptySlicesOverMissingIni_WritesWithoutConfirmAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
            var viewModel = CreateViewModel(dialogs: dialogs);
            viewModel.AtlasPath = Path.Combine(directory, "atlas.tga");
            using var stream = new MemoryStream(ValidPngBytes);
            using var bitmap = new Bitmap(stream);
            viewModel.AtlasBitmap = bitmap;

            await viewModel.SaveCommand.ExecuteAsync(null);

            string iniPath = Path.Combine(directory, "atlas.ini");
            Assert.True(File.Exists(iniPath));
            Assert.StartsWith("; ", await File.ReadAllTextAsync(iniPath));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that saving slices with case-insensitively duplicated names shows an error and skips the write.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task SaveCommand_DuplicateSliceNames_ShowsErrorAndSkipsWriteAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var notifications = new Mock<INotificationService>();
            var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
            var viewModel = CreateViewModel(notifications: notifications, dialogs: dialogs);
            viewModel.AtlasPath = Path.Combine(directory, "atlas.tga");
            using var stream = new MemoryStream(ValidPngBytes);
            using var bitmap = new Bitmap(stream);
            viewModel.AtlasBitmap = bitmap;
            viewModel.Slices.Add(new TextureSliceViewModel(new MappedImageDefinition("Hero", "atlas.tga", 1, 1, 0, 0, 1, 1)));
            viewModel.Slices.Add(new TextureSliceViewModel(new MappedImageDefinition("hero", "atlas.tga", 1, 1, 0, 0, 1, 1)));

            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.False(File.Exists(Path.Combine(directory, "atlas.ini")));
            notifications.Verify(
                notification => notification.ShowError(It.IsAny<string>(), It.Is<string>(message => message.Contains("duplicated")), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that saving a slice with a blank name shows an error and skips the write.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task SaveCommand_BlankSliceName_ShowsErrorAndSkipsWriteAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var notifications = new Mock<INotificationService>();
            var dialogs = new Mock<IDialogService>(MockBehavior.Strict);
            var viewModel = CreateViewModel(notifications: notifications, dialogs: dialogs);
            viewModel.AtlasPath = Path.Combine(directory, "atlas.tga");
            using var stream = new MemoryStream(ValidPngBytes);
            using var bitmap = new Bitmap(stream);
            viewModel.AtlasBitmap = bitmap;
            viewModel.Slices.Add(new TextureSliceViewModel(new MappedImageDefinition("  ", "atlas.tga", 1, 1, 0, 0, 1, 1)));

            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.False(File.Exists(Path.Combine(directory, "atlas.ini")));
            notifications.Verify(
                notification => notification.ShowError(It.IsAny<string>(), It.Is<string>(message => message.Contains("empty name")), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that a partial scan warns instead of failing, since the registry commits the valid entries.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ScanRegistry_PartialFailure_WarnsInsteadOfFailingAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var registry = new Mock<IMappedImageRegistry>();
            registry.Setup(mock => mock.ScanDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<MappedImageScanResult>.CreateFailure(
                    ["icons.ini: malformed block"],
                    new MappedImageScanResult(2, 5),
                    TimeSpan.Zero));
            registry.Setup(mock => mock.All).Returns(new List<MappedImageDefinition>());
            registry.Setup(mock => mock.GetByTexture(It.IsAny<string>())).Returns(new List<MappedImageDefinition>());
            var notifications = new Mock<INotificationService>();
            var viewModel = CreateViewModel(notifications: notifications, registry: registry);
            viewModel.FileExplorer.Directory = directory;
            using var bitmap = OpenAtlas(viewModel, "atlas.tga");

            await viewModel.ScanRegistryCommand.ExecuteAsync(null);

            notifications.Verify(
                notification => notification.ShowWarning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
            notifications.Verify(
                notification => notification.ShowError(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Never);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that browsing a folder scans mapped images and opens the first texture,
    /// so the header Open folder button behaves like the Files tab browse action.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task BrowseFolder_AdoptedFlow_ScansAndOpensFirstTextureAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "atlas.png"), ValidPngBytes);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "atlas.ini"),
                "MappedImage Hero\n  Texture = atlas.png\n  TextureWidth = 1\n  TextureHeight = 1\n  Coords = Left:0 Top:0 Right:1 Bottom:1\n  Status = NONE\nEnd\n");

            var bitmapService = new TextureBitmapService(
                new Mock<ISageTextureCodec>().Object,
                NullLogger<TextureBitmapService>.Instance);
            var viewModel = new TextureEditorViewModel(
                new SageMappedImageParser(NullLogger<SageMappedImageParser>.Instance),
                new MappedImageRegistry(
                    new SageMappedImageParser(NullLogger<SageMappedImageParser>.Instance),
                    NullLogger<MappedImageRegistry>.Instance),
                new Mock<IAtlasPackingService>().Object,
                new Mock<ITextureImageLoader>().Object,
                bitmapService,
                new Mock<INotificationService>().Object,
                NullLogger<TextureEditorViewModel>.Instance,
                new Mock<ILocalizationService>().Object,
                new Mock<IDialogService>().Object);
            viewModel.FileExplorer.BrowseFolderAsync = _ => Task.FromResult<string?>(directory);

            await viewModel.FileExplorer.BrowseCommand.ExecuteAsync(null);

            Assert.NotNull(viewModel.AtlasBitmap);
            Assert.Equal("atlas.png", viewModel.AtlasFileName);
            Assert.NotEmpty(viewModel.RegistryImages);
            var slice = Assert.Single(viewModel.Slices);
            Assert.Equal("Hero", slice.Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that opening a same-named texture in another folder does not adopt
    /// stale registry slices authored for the first folder.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task OpenSameNamedTextureInAnotherFolder_DoesNotAdoptStaleSlicesAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string first = Path.Combine(root, "dir1");
        string second = Path.Combine(root, "dir2");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(first, "icons.png"), ValidPngBytes);
            await File.WriteAllTextAsync(
                Path.Combine(first, "icons.ini"),
                "MappedImage Hero\n  Texture = icons.png\n  TextureWidth = 1\n  TextureHeight = 1\n  Coords = Left:0 Top:0 Right:1 Bottom:1\n  Status = NONE\nEnd\n");
            await File.WriteAllBytesAsync(Path.Combine(second, "icons.png"), ValidPngBytes);

            var viewModel = CreateViewModelWithRegistry();
            viewModel.FileExplorer.OpenFileCommand.Execute(new EditorFileTreeNodeViewModel("icons.png", Path.Combine(first, "icons.png"), false));
            await WaitForAtlasAsync(viewModel, Path.Combine(first, "icons.png"));

            var adopted = Assert.Single(viewModel.Slices);
            Assert.Equal("Hero", adopted.Name);

            viewModel.FileExplorer.OpenFileCommand.Execute(new EditorFileTreeNodeViewModel("icons.png", Path.Combine(second, "icons.png"), false));
            await WaitForAtlasAsync(viewModel, Path.Combine(second, "icons.png"));

            Assert.Equal("icons.png", viewModel.AtlasFileName);
            Assert.Empty(viewModel.Slices);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies that explicitly importing an INI adopts its entries even when the INI
    /// lives in another folder, since the user just pointed at that file.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ImportIniFromAnotherFolder_AdoptsItsEntriesAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string first = Path.Combine(root, "dir1");
        string second = Path.Combine(root, "dir2");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(first, "icons.png"), ValidPngBytes);
            await File.WriteAllTextAsync(
                Path.Combine(first, "icons.ini"),
                "MappedImage Hero\n  Texture = icons.png\n  TextureWidth = 1\n  TextureHeight = 1\n  Coords = Left:0 Top:0 Right:1 Bottom:1\n  Status = NONE\nEnd\n");
            await File.WriteAllBytesAsync(Path.Combine(second, "icons.png"), ValidPngBytes);

            var viewModel = CreateViewModelWithRegistry();
            viewModel.FileExplorer.OpenFileCommand.Execute(new EditorFileTreeNodeViewModel("icons.png", Path.Combine(second, "icons.png"), false));
            await WaitForAtlasAsync(viewModel, Path.Combine(second, "icons.png"));
            Assert.Empty(viewModel.Slices);

            viewModel.FileExplorer.OpenFileCommand.Execute(new EditorFileTreeNodeViewModel("icons.ini", Path.Combine(first, "icons.ini"), false));
            for (int attempt = 0; attempt < 500 && viewModel.Slices.Count == 0; attempt++)
            {
                Dispatcher.UIThread.RunJobs(null);
                await Task.Delay(20);
            }

            var adopted = Assert.Single(viewModel.Slices);
            Assert.Equal("Hero", adopted.Name);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies that opening an INI while slices exist loads the matching slices
    /// and reports that the new entries landed in the library.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ImportIniWithExistingSlices_LoadsMatchingSlicesAndReportsLibraryAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        string first = Path.Combine(root, "dir1");
        string second = Path.Combine(root, "dir2");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(first, "icons.png"), ValidPngBytes);
            await File.WriteAllTextAsync(
                Path.Combine(first, "icons.ini"),
                "MappedImage Hero\n  Texture = icons.png\n  TextureWidth = 1\n  TextureHeight = 1\n  Coords = Left:0 Top:0 Right:1 Bottom:1\n  Status = NONE\nEnd\n");
            string moreIni =
                "MappedImage Villain\n  Texture = icons.png\n  TextureWidth = 1\n  TextureHeight = 1\n  Coords = Left:0 Top:0 Right:1 Bottom:1\n  Status = NONE\nEnd\n" +
                "MappedImage Stranger\n  Texture = other.png\n  TextureWidth = 1\n  TextureHeight = 1\n  Coords = Left:0 Top:0 Right:1 Bottom:1\n  Status = NONE\nEnd\n";
            await File.WriteAllTextAsync(Path.Combine(second, "more.ini"), moreIni);

            var notifications = new Mock<INotificationService>();
            var viewModel = CreateViewModelWithRegistry(notifications);
            viewModel.FileExplorer.OpenFileCommand.Execute(new EditorFileTreeNodeViewModel("icons.png", Path.Combine(first, "icons.png"), false));
            await WaitForAtlasAsync(viewModel, Path.Combine(first, "icons.png"));
            Assert.Single(viewModel.Slices);

            viewModel.FileExplorer.OpenFileCommand.Execute(new EditorFileTreeNodeViewModel("more.ini", Path.Combine(second, "more.ini"), false));
            for (int attempt = 0; attempt < 500 && viewModel.RegistryImages.Count < 3; attempt++)
            {
                Dispatcher.UIThread.RunJobs(null);
                await Task.Delay(20);
            }

            var loaded = Assert.Single(viewModel.Slices);
            Assert.Equal("Villain", loaded.Name);
            Assert.Contains(viewModel.RegistryImages, image => image.Name == "Villain");
            Assert.Contains(viewModel.RegistryImages, image => image.Name == "Stranger");
            notifications.Verify(
                notification => notification.ShowSuccess(It.IsAny<string>(), It.Is<string>(message => message.Contains("Loaded 1 slices")), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Verifies that explicitly requesting an edit for a registry entry whose texture is missing
    /// on disk opens a placeholder canvas and adopts the slice.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task LoadRegistryEntry_ExplicitOpen_MissingTexture_OpensPlaceholderAsync()
    {
        var notifications = new Mock<INotificationService>();
        var viewModel = CreateViewModel(notifications: notifications);
        var definition = new MappedImageDefinition("MissingImage", "MissingTexture.tga", 512, 256, 10, 10, 50, 50);

        viewModel.LoadRegistryEntry(definition, explicitOpen: true);

        for (int attempt = 0; attempt < 500 && (viewModel.AtlasBitmap is null || viewModel.IsBusy); attempt++)
        {
            Dispatcher.UIThread.RunJobs(null);
            await Task.Delay(20);
        }

        Assert.NotNull(viewModel.AtlasBitmap);
        Assert.Equal(512, viewModel.AtlasBitmap.PixelSize.Width);
        Assert.Equal(256, viewModel.AtlasBitmap.PixelSize.Height);
        var slice = Assert.Single(viewModel.Slices);
        Assert.Equal("MissingImage", slice.Name);
        Assert.Same(slice, viewModel.SelectedSlice);
        notifications.Verify(
            notification => notification.ShowInfo(It.IsAny<string>(), It.Is<string>(message => message.Contains("placeholder")), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that opening an INI file whose texture cannot be found on disk
    /// synthesizes a placeholder canvas and loads all slices for that texture.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task OpenIni_NoTextureOnDisk_OpensPlaceholderAndAdoptsSlicesAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            string iniPath = Path.Combine(root, "orphan.ini");
            string iniContent =
                "MappedImage SliceOne\n  Texture = Ghost.tga\n  TextureWidth = 640\n  TextureHeight = 480\n  Coords = Left:0 Top:0 Right:100 Bottom:100\n  Status = NONE\nEnd\n" +
                "MappedImage SliceTwo\n  Texture = Ghost.tga\n  TextureWidth = 640\n  TextureHeight = 480\n  Coords = Left:100 Top:100 Right:200 Bottom:200\n  Status = NONE\nEnd\n";
            await File.WriteAllTextAsync(iniPath, iniContent);

            var notifications = new Mock<INotificationService>();
            var viewModel = CreateViewModelWithRegistry(notifications);

            viewModel.FileExplorer.OpenFileCommand.Execute(new EditorFileTreeNodeViewModel("orphan.ini", iniPath, false));

            for (int attempt = 0; attempt < 500 && (viewModel.AtlasBitmap is null || viewModel.IsBusy); attempt++)
            {
                Dispatcher.UIThread.RunJobs(null);
                await Task.Delay(20);
            }

            Assert.NotNull(viewModel.AtlasBitmap);
            Assert.Equal(640, viewModel.AtlasBitmap.PixelSize.Width);
            Assert.Equal(480, viewModel.AtlasBitmap.PixelSize.Height);
            Assert.Equal(2, viewModel.Slices.Count);
            notifications.Verify(
                notification => notification.ShowInfo(It.IsAny<string>(), It.Is<string>(message => message.Contains("placeholder")), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task WaitForAtlasAsync(TextureEditorViewModel viewModel, string expectedPath)
    {
        for (int attempt = 0; attempt < 500 && (viewModel.AtlasPath != expectedPath || viewModel.IsBusy); attempt++)
        {
            Dispatcher.UIThread.RunJobs(null);
            await Task.Delay(20);
        }
    }

    private static TextureEditorViewModel CreateViewModelWithRegistry(Mock<INotificationService>? notifications = null)
    {
        var bitmapService = new TextureBitmapService(
            new Mock<ISageTextureCodec>().Object,
            NullLogger<TextureBitmapService>.Instance);
        return new TextureEditorViewModel(
            new SageMappedImageParser(NullLogger<SageMappedImageParser>.Instance),
            new MappedImageRegistry(
                new SageMappedImageParser(NullLogger<SageMappedImageParser>.Instance),
                NullLogger<MappedImageRegistry>.Instance),
            new Mock<IAtlasPackingService>().Object,
            new Mock<ITextureImageLoader>().Object,
            bitmapService,
            (notifications ?? new Mock<INotificationService>()).Object,
            NullLogger<TextureEditorViewModel>.Instance,
            new Mock<ILocalizationService>().Object,
            new Mock<IDialogService>().Object);
    }

    private static Bitmap OpenAtlas(TextureEditorViewModel viewModel, string fileName)
    {
        using var stream = new MemoryStream(ValidPngBytes);
        var bitmap = new Bitmap(stream);
        viewModel.AtlasPath = Path.Combine(Path.GetTempPath(), fileName);
        viewModel.AtlasBitmap = bitmap;
        return bitmap;
    }

    private static TextureEditorViewModel CreateViewModel(Mock<INotificationService>? notifications = null, Mock<IDialogService>? dialogs = null, Mock<IMappedImageRegistry>? registry = null)
    {
        var bitmapService = new TextureBitmapService(
            new Mock<ISageTextureCodec>().Object,
            NullLogger<TextureBitmapService>.Instance);
        var registryMock = registry ?? new Mock<IMappedImageRegistry>();
        if (registry is null)
        {
            // Opening an atlas rebuilds the picker from the registry catalog.
            registryMock.Setup(mock => mock.All).Returns(new List<MappedImageDefinition>());
            registryMock.Setup(mock => mock.GetByTexture(It.IsAny<string>())).Returns(Array.Empty<MappedImageDefinition>());
        }

        return new TextureEditorViewModel(
            new SageMappedImageParser(NullLogger<SageMappedImageParser>.Instance),
            registryMock.Object,
            new Mock<IAtlasPackingService>().Object,
            new Mock<ITextureImageLoader>().Object,
            bitmapService,
            (notifications ?? new Mock<INotificationService>()).Object,
            NullLogger<TextureEditorViewModel>.Instance,
            new Mock<ILocalizationService>().Object,
            (dialogs ?? new Mock<IDialogService>()).Object);
    }
}
