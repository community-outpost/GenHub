using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAssertions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Features.Tools.WndEditor.Services;
using GenHub.Features.Tools.WndEditor.ViewModels;
using GenHub.Features.Tools.WndEditor.Views;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.WndEditor.Views;

/// <summary>
/// Headless load tests for WND editor views.
/// </summary>
public sealed class WndEditorViewsTests
{
    /// <summary>
    /// Tests that the editor view loads with toolbar tools and canvas parts.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task WndEditorView_Loads_WithToolbarAndCanvas()
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
        var window = new Window { Width = 1600, Height = 900, Content = view };

        try
        {
            // Act
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            // Assert
            view.FindControl<ScrollViewer>("CanvasScrollViewer").Should().NotBeNull();
            view.GetVisualDescendants().OfType<ToggleButton>().Should().NotBeEmpty();
            view.GetVisualDescendants().OfType<ComboBox>().Should().NotBeEmpty();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Tests that the general properties view renders color swatches with picker flyouts.
    /// </summary>
    [AvaloniaFact]
    public void WndGeneralPropertiesView_Loads_WithColorSwatch()
    {
        // Arrange
        var window = new WndWindow();
        window.SetProperty(
            "TEXTCOLOR",
            new WndTextColorValue(
                WndRgbaColor.White,
                WndRgbaColor.White,
                WndRgbaColor.White,
                WndRgbaColor.White,
                WndRgbaColor.White,
                WndRgbaColor.White).ToString());
        var viewModel = new WndWindowPropertiesViewModel(
            window,
            new WndDocumentService(Mock.Of<ILogger<WndDocumentService>>()),
            Mock.Of<INotificationService>(),
            CreateLocalizationService(),
            (key, value) => { },
            key => { },
            properties => { });
        var view = new WndGeneralPropertiesView { DataContext = viewModel };
        var host = new Window { Width = 600, Height = 900, Content = view };

        try
        {
            // Act
            host.Show();
            Dispatcher.UIThread.RunJobs(null);
            foreach (var expander in view.GetVisualDescendants().OfType<Expander>())
            {
                expander.IsExpanded = true;
            }

            Dispatcher.UIThread.RunJobs(null);

            // Assert
            var swatch = view.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => button.Flyout is Flyout flyout && flyout.Content is ColorView);
            swatch.Should().NotBeNull();
            var colorView = (ColorView)((Flyout)swatch!.Flyout!).Content!;
            colorView.MaxWidth.Should().Be(360);
        }
        finally
        {
            host.Close();
        }
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
            new WndEditorAssetService(Mock.Of<IWndImageAssetService>(), Mock.Of<IWndStringTableService>()),
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
