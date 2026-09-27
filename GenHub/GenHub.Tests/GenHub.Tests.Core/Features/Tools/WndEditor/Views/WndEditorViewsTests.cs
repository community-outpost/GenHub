using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
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

    /// <summary>
    /// Tests that inspector sections stack with zero spacing and collapsed sections carry no dead space.
    /// </summary>
    [AvaloniaFact]
    public void WndPropertiesViews_Sections_HaveZeroSpacing()
    {
        // Arrange: load the compact expander styles like the application does.
        var style = new StyleInclude(new Uri("avares://GenHub/"))
        {
            Source = new Uri("avares://GenHub/Assets/Styles/ExpanderStyles.axaml"),
        };
        Avalonia.Application.Current!.Styles.Add(style);
        try
        {
            var window = new WndWindow();
            window.SetProperty("NAME", "TestWindow");
            var viewModel = new WndWindowPropertiesViewModel(
                window,
                new WndDocumentService(Mock.Of<ILogger<WndDocumentService>>()),
                Mock.Of<INotificationService>(),
                CreateLocalizationService(),
                (key, value) => { },
                key => { },
                properties => { });

            // Act + Assert: the general view always lays out every section, so it
            // carries the layout assertions; the control view gates sections by
            // control type, so it carries the style assertions.
            AssertZeroSectionSpacing(new WndGeneralPropertiesView { DataContext = viewModel, Width = 400 }, expectLayout: true);
            viewModel.ControlKind = WndControlType.ScrollListBox;
            AssertZeroSectionSpacing(new WndControlPropertiesView { DataContext = viewModel, Width = 400 }, expectLayout: false);
        }
        finally
        {
            Avalonia.Application.Current!.Styles.Remove(style);
        }
    }

    private static void AssertZeroSectionSpacing(UserControl view, bool expectLayout)
    {
        var host = new Window { Width = 600, Height = 1200, Content = view };
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs(null);

            var all = view.GetVisualDescendants()
                .OfType<Expander>()
                .Where(expander => expander.Classes.Contains("compact"))
                .ToList();
            all.Should().NotBeEmpty();

            // The compact style must zero the box model: the default Fluent
            // Expander MinHeight (48) otherwise leaves dead space in every
            // collapsed section.
            foreach (var section in all)
            {
                section.MinHeight.Should().Be(0);
                section.Margin.Should().Be(new Thickness(0));
                section.Padding.Should().Be(new Thickness(0));
            }

            if (!expectLayout)
            {
                return;
            }

            var sections = all.Where(expander => expander.Bounds.Height > 0).ToList();
            sections.Should().NotBeEmpty();

            // Sibling sections must touch with no gap between them.
            foreach (var group in sections.GroupBy(expander => expander.Parent))
            {
                var ordered = group.OrderBy(expander => expander.Bounds.Position.Y).ToList();
                for (var i = 1; i < ordered.Count; i++)
                {
                    var gap = ordered[i].Bounds.Position.Y - (ordered[i - 1].Bounds.Position.Y + ordered[i - 1].Bounds.Height);
                    gap.Should().Be(0);
                }
            }

            // Collapsed sections must be exactly header-sized (no dead template space).
            foreach (var collapsed in sections.Where(expander => !expander.IsExpanded))
            {
                var header = collapsed.GetVisualDescendants()
                    .OfType<ToggleButton>()
                    .First(toggle => toggle.Name == "ExpanderHeader");
                collapsed.Bounds.Height.Should().Be(header.Bounds.Height);
            }
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
            Mock.Of<IWndTextureImportService>(),
            Mock.Of<IChallengeMedalService>(),
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
