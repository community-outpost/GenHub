using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GenHub.Common.Editors;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.TextureEditor;
using GenHub.Features.Tools.TextureEditor.Services;
using GenHub.Features.Tools.TextureEditor.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
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
            for (int attempt = 0; attempt < 200 && viewModel.Slices.Count == 0; attempt++)
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
    /// Verifies that importing an INI while slices exist keeps the slices and
    /// reports that the new entries landed in the library.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task ImportIniWithExistingSlices_KeepsSlicesAndReportsLibraryAsync()
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
            for (int attempt = 0; attempt < 200 && viewModel.RegistryImages.Count < 3; attempt++)
            {
                Dispatcher.UIThread.RunJobs(null);
                await Task.Delay(20);
            }

            var kept = Assert.Single(viewModel.Slices);
            Assert.Equal("Hero", kept.Name);
            Assert.Contains(viewModel.RegistryImages, image => image.Name == "Villain");
            Assert.Contains(viewModel.RegistryImages, image => image.Name == "Stranger");
            notifications.Verify(
                notification => notification.ShowSuccess(It.IsAny<string>(), It.Is<string>(message => message.Contains("kept")), It.IsAny<int?>(), It.IsAny<bool>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task WaitForAtlasAsync(TextureEditorViewModel viewModel, string expectedPath)
    {
        for (int attempt = 0; attempt < 200 && (viewModel.AtlasPath != expectedPath || viewModel.IsBusy); attempt++)
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

    private static TextureEditorViewModel CreateViewModel(Mock<INotificationService>? notifications = null, Mock<IDialogService>? dialogs = null)
    {
        var bitmapService = new TextureBitmapService(
            new Mock<ISageTextureCodec>().Object,
            NullLogger<TextureBitmapService>.Instance);
        return new TextureEditorViewModel(
            new SageMappedImageParser(NullLogger<SageMappedImageParser>.Instance),
            new Mock<IMappedImageRegistry>().Object,
            new Mock<IAtlasPackingService>().Object,
            new Mock<ITextureImageLoader>().Object,
            bitmapService,
            (notifications ?? new Mock<INotificationService>()).Object,
            NullLogger<TextureEditorViewModel>.Instance,
            new Mock<ILocalizationService>().Object,
            (dialogs ?? new Mock<IDialogService>()).Object);
    }
}
