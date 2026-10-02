using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Features.Tools.RmlEditor.Services;
using GenHub.Features.Tools.RmlEditor.ViewModels;
using GenHub.Features.Tools.RmlEditor.Views;
using GenHub.Features.Tools.TextureEditor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.RmlEditor.Views;

/// <summary>
/// Headless load tests for RML editor views.
/// </summary>
public sealed class RmlEditorViewsTests : IDisposable
{
    private const string SampleDocument =
        "<rml>\n" +
        "  <head><title>Menu</title></head>\n" +
        "  <body><div id=\"panel\"><button id=\"start\">Start</button></div></body>\n" +
        "</rml>\n";

    private readonly string _tempDirectory;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RmlEditorViewsTests"/> class.
    /// </summary>
    public RmlEditorViewsTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Tests that the editor view loads with toolbar tools and canvas parts.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task RmlEditorView_Loads_WithToolbarAndCanvasAsync()
    {
        var samplePath = Path.Combine(_tempDirectory, "Menu.rml");
        await File.WriteAllTextAsync(samplePath, SampleDocument);
        var viewModel = CreateEditorViewModel();
        await viewModel.OpenFileAsync(samplePath);
        var view = new RmlEditorView { DataContext = viewModel };
        var window = new Window { Width = 1600, Height = 900, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            view.GetVisualDescendants().OfType<Button>().Should().NotBeEmpty();
            view.GetVisualDescendants().OfType<TreeView>().Should().NotBeEmpty();
            view.GetVisualDescendants().OfType<TabControl>().Should().NotBeEmpty();
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    }

    /// <summary>
    /// Tests that the add-element submenu shows each tag name.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task RmlEditorView_AddElementMenu_ShowsTagNamesAsync()
    {
        var samplePath = Path.Combine(_tempDirectory, "Menu.rml");
        await File.WriteAllTextAsync(samplePath, SampleDocument);
        var viewModel = CreateEditorViewModel();
        await viewModel.OpenFileAsync(samplePath);
        var view = new RmlEditorView { DataContext = viewModel };
        var window = new Window { Width = 1600, Height = 900, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);
            var button = view.GetVisualDescendants().OfType<Button>().First(b => b.Flyout is MenuFlyout);
            var flyout = (MenuFlyout)button.Flyout!;
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs(null);

            var addElement = flyout.Items.OfType<MenuItem>().First(m => m.Items.Count > 0);
            var subs = addElement.Items.OfType<MenuItem>().ToList();
            subs.Should().HaveCount(11);
            foreach (var sub in subs)
            {
                sub.Header.Should().Be(sub.CommandParameter);
                sub.Command.Should().Be(viewModel.AddChildElementCommand);
            }

            flyout.Hide();
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    }

    /// <summary>
    /// Releases test resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Directory.Delete(_tempDirectory, recursive: true);
    }

    private static RmlEditorViewModel CreateEditorViewModel()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(s => s.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] args) => args is { Length: > 0 } ? $"{key} {string.Join(' ', args)}" : key);
        var documents = new RmlDocumentService(new Mock<ILogger<RmlDocumentService>>().Object);
        var styles = new RcssDocumentService(new Mock<ILogger<RcssDocumentService>>().Object);
        var builder = new RmlPreviewBuilder(styles, localization.Object);
        var bitmaps = new TextureBitmapService(new Mock<ISageTextureCodec>().Object, new Mock<ILogger<TextureBitmapService>>().Object);
        var images = new RmlImageResolver(bitmaps, new Mock<ILogger<RmlImageResolver>>().Object);
        return new RmlEditorViewModel(
            documents,
            styles,
            builder,
            images,
            new Mock<INotificationService>().Object,
            localization.Object,
            new Mock<IDialogService>().Object,
            new Mock<ILogger<RmlEditorViewModel>>().Object);
    }
}
