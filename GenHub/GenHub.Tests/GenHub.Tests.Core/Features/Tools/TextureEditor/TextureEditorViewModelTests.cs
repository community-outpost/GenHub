using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
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

    private static Bitmap OpenAtlas(TextureEditorViewModel viewModel, string fileName)
    {
        using var stream = new MemoryStream(ValidPngBytes);
        var bitmap = new Bitmap(stream);
        viewModel.AtlasPath = Path.Combine(Path.GetTempPath(), fileName);
        viewModel.AtlasBitmap = bitmap;
        return bitmap;
    }
}
