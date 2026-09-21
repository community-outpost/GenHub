using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.WndEditor.Services;
using GenHub.Features.Tools.WndEditor.ViewModels;
using GenHub.Features.Tools.WndEditor.Views;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Views;

/// <summary>
/// Headless interaction tests for WND editor canvas panning.
/// </summary>
public sealed class WndEditorPanTests
{
    /// <summary>
    /// Tests that middle-drag pans the canvas scroll viewer.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task Canvas_MiddleDrag_PansScrollViewer()
    {
        // Arrange
        var viewModel = CreateEditorViewModel();
        await viewModel.LoadFromTextAsync(
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = USER;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 1600 1200, CREATIONRESOLUTION: 800 600;\n" +
            "END\n",
            null);
        var view = new WndEditorView { DataContext = viewModel };
        var window = new Window { Width = 1000, Height = 700, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);
            var scroller = view.FindControl<ScrollViewer>("CanvasScrollViewer");
            scroller.Should().NotBeNull();
            var start = CenterOnWindow(scroller!, window);

            // Act
            window.MouseDown(start, MouseButton.Middle);
            Dispatcher.UIThread.RunJobs(null);
            window.MouseMove(start + new Point(-120, -80));
            Dispatcher.UIThread.RunJobs(null);
            window.MouseUp(start + new Point(-120, -80), MouseButton.Middle);
            Dispatcher.UIThread.RunJobs(null);

            // Assert
            scroller!.Offset.X.Should().BeGreaterThan(0);
            scroller.Offset.Y.Should().BeGreaterThan(0);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Tests that left-drag pans while pan mode is active.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task Canvas_PanModeLeftDrag_PansScrollViewer()
    {
        // Arrange
        var viewModel = CreateEditorViewModel();
        await viewModel.LoadFromTextAsync(
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = USER;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 1600 1200, CREATIONRESOLUTION: 800 600;\n" +
            "END\n",
            null);
        viewModel.IsPanMode = true;
        var view = new WndEditorView { DataContext = viewModel };
        var window = new Window { Width = 1000, Height = 700, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);
            var scroller = view.FindControl<ScrollViewer>("CanvasScrollViewer");
            scroller.Should().NotBeNull();
            var start = CenterOnWindow(scroller!, window);

            // Act
            window.MouseDown(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs(null);
            window.MouseMove(start + new Point(-120, -80));
            Dispatcher.UIThread.RunJobs(null);
            window.MouseUp(start + new Point(-120, -80), MouseButton.Left);
            Dispatcher.UIThread.RunJobs(null);

            // Assert
            scroller!.Offset.X.Should().BeGreaterThan(0);
            scroller.Offset.Y.Should().BeGreaterThan(0);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Tests that zooming grows the canvas visual so a fitting document becomes pannable.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task Canvas_ZoomIn_GrowsCanvasVisual()
    {
        // Arrange
        var viewModel = CreateEditorViewModel();
        await viewModel.LoadFromTextAsync(
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = USER;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 800 600, CREATIONRESOLUTION: 800 600;\n" +
            "END\n",
            null);
        var view = new WndEditorView { DataContext = viewModel };
        var window = new Window { Width = 2000, Height = 1200, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);
            var canvas = view.GetVisualDescendants().OfType<Canvas>().FirstOrDefault();
            canvas.Should().NotBeNull();
            var before = canvas!.Bounds.Width;

            // Act
            viewModel.ZoomInCommand.Execute(null);
            Dispatcher.UIThread.RunJobs(null);

            // Assert
            canvas.Bounds.Width.Should().BeGreaterThan(before);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Tests that a document fitting the viewport is still pannable thanks to canvas padding.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task Canvas_FittingDocument_RemainsPannable()
    {
        // Arrange
        var viewModel = CreateEditorViewModel();
        await viewModel.LoadFromTextAsync(
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = USER;\n" +
            "  SCREENRECT = UPPERLEFT: 0 0, BOTTOMRIGHT: 800 600, CREATIONRESOLUTION: 800 600;\n" +
            "END\n",
            null);
        var view = new WndEditorView { DataContext = viewModel };
        var window = new Window { Width = 2000, Height = 1200, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);
            var scroller = view.FindControl<ScrollViewer>("CanvasScrollViewer");
            scroller.Should().NotBeNull();
            scroller!.Offset = new Vector(200, 150);
            Dispatcher.UIThread.RunJobs(null);
            var start = CenterOnWindow(scroller, window);

            // Act
            window.MouseDown(start, MouseButton.Middle);
            Dispatcher.UIThread.RunJobs(null);
            window.MouseMove(start + new Point(-120, -80));
            Dispatcher.UIThread.RunJobs(null);
            window.MouseUp(start + new Point(-120, -80), MouseButton.Middle);
            Dispatcher.UIThread.RunJobs(null);

            // Assert
            scroller.Offset.X.Should().BeApproximately(320, 1);
            scroller.Offset.Y.Should().BeApproximately(230, 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Tests that Ctrl+Z undoes a canvas drag and Ctrl+Y redoes it.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task Canvas_CtrlZUndoRedo_RestoresDraggedPosition()
    {
        // Arrange
        var viewModel = CreateEditorViewModel();
        await viewModel.LoadFromTextAsync(
            "FILE_VERSION = 2;\n" +
            "WINDOW\n" +
            "  WINDOWTYPE = PUSHBUTTON;\n" +
            "  SCREENRECT = UPPERLEFT: 10 20, BOTTOMRIGHT: 110 60, CREATIONRESOLUTION: 800 600;\n" +
            "END\n",
            null);
        var view = new WndEditorView { DataContext = viewModel };
        var window = new Window { Width = 1400, Height = 900, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);
            var focusTarget = view.GetVisualDescendants().OfType<Button>().FirstOrDefault();
            focusTarget.Should().NotBeNull();
            focusTarget!.Focus();
            var item = viewModel.CanvasItems[0];
            viewModel.BeginCanvasDrag(item, new Point(item.X, item.Y));
            viewModel.UpdateCanvasDrag(new Point(item.X + 50, item.Y + 30));
            viewModel.EndCanvasDrag();
            viewModel.CanvasItems[0].Window.TryGetScreenRect(out var moved).Should().BeTrue();
            moved!.UpperLeftX.Should().Be(60);

            // Act
            window.KeyPressQwerty(PhysicalKey.Z, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs(null);

            // Assert
            viewModel.CanvasItems[0].Window.TryGetScreenRect(out var undone).Should().BeTrue();
            undone!.UpperLeftX.Should().Be(10);
            undone.UpperLeftY.Should().Be(20);
            viewModel.CanUndo.Should().BeFalse();
            viewModel.CanRedo.Should().BeTrue();

            // Act
            window.KeyPressQwerty(PhysicalKey.Y, RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs(null);

            // Assert
            viewModel.CanvasItems[0].Window.TryGetScreenRect(out var redone).Should().BeTrue();
            redone!.UpperLeftX.Should().Be(60);
            redone.UpperLeftY.Should().Be(50);
        }
        finally
        {
            window.Close();
        }
    }

    private static Point CenterOnWindow(ScrollViewer scroller, Window window)
    {
        var center = scroller.TranslatePoint(
            new Point(scroller.Bounds.Width / 2, scroller.Bounds.Height / 2),
            window);
        center.Should().NotBeNull();
        return center!.Value;
    }

    private static WndEditorViewModel CreateEditorViewModel()
    {
        var gameInstallService = new Mock<IGameInstallationService>();
        gameInstallService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([]));
        return new WndEditorViewModel(
            new WndDocumentService(Mock.Of<ILogger<WndDocumentService>>()),
            Mock.Of<INotificationService>(),
            CreateLocalizationService(),
            Mock.Of<IDialogService>(),
            gameInstallService.Object,
            Mock.Of<IWndImageAssetService>(),
            Mock.Of<IWndStringTableService>(),
            Mock.Of<ILogger<WndEditorViewModel>>());
    }

    private static ILocalizationService CreateLocalizationService()
    {
        var mock = new Mock<ILocalizationService>();
        mock.Setup(service => service.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns<string, object?[]>((key, args) => key);
        return mock.Object;
    }
}
