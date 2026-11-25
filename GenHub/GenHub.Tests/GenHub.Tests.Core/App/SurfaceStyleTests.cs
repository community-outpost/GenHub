using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Features.GameProfiles.Views;
using GenHub.Features.Info.ViewModels;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.App;

/// <summary>Checks readable profile surfaces and bounded identity artwork.</summary>
public class SurfaceStyleTests
{
    /// <summary>Changing profile identity colors cannot wash out the sidebar or the form's reading surface.</summary>
    /// <param name="accent">A profile color, including bright and dark extremes.</param>
    [AvaloniaTheory]
    [InlineData("#FFFFFF")]
    [InlineData("#FFFF00")]
    [InlineData("#000000")]
    [InlineData("#9C27B0")]
    public void ProfileSettings_KeepOpaqueNeutralSurfacesAndAccentIdentity(string accent)
    {
        var viewModel = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ProfileTab();
        var view = new GameProfileSettingsContentView { DataContext = viewModel };
        var window = new Window { Width = 1100, Height = 800, Content = view };
        window.Show();
        try
        {
            viewModel.ColorValue = accent;
            Dispatcher.UIThread.RunJobs();
            var cardFill = ResourceColor("CardBackground");
            Assert.Equal((byte)255, cardFill.A);

            var sidebar = view.GetVisualDescendants().OfType<GameProfileSettingsSidebarView>().Single();
            Assert.Equal(cardFill, BrushColor(sidebar.FindControl<Border>("SidebarSurface")!.Background));
            Assert.Equal(Color.Parse(accent), BrushColor(sidebar.FindControl<Border>("ProfileIconRing")!.BorderBrush));

            var cards = view.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("settings-card") && border.IsEffectivelyVisible).ToArray();
            Assert.NotEmpty(cards);
            var cornerRadius = (CornerRadius)Avalonia.Application.Current!.FindResource("CardCornerRadius")!;
            Assert.All(cards, card =>
            {
                Assert.Equal(cardFill, BrushColor(card.Background));
                Assert.Equal(cornerRadius, card.CornerRadius);
            });
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>At the window's minimum width each General row, including the expanded Advanced rows, keeps a readable label beside its control.</summary>
    [AvaloniaFact]
    public void GeneralPage_AtMinimumWindowWidth_KeepsLabelsBesideControls()
    {
        var viewModel = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ProfileTab();
        var view = new GameProfileGeneralSettingsView { DataContext = viewModel };
        var window = new Window { Width = 720 - 240, Height = 800, Content = view };
        window.Show();
        try
        {
            view.FindControl<Expander>("AdvancedSection")!.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var rows = view.GetVisualDescendants().OfType<Grid>()
                .Where(grid => grid.ColumnDefinitions.Count == 2 && grid.ColumnDefinitions[1].MaxWidth < double.PositiveInfinity && grid.IsEffectivelyVisible)
                .ToArray();
            Assert.Equal(3, rows.Length);
            Assert.All(rows, row =>
            {
                var label = row.GetVisualDescendants().OfType<TextBlock>().First(item => item.Classes.Contains("row-label"));
                var control = row.Children.OfType<Control>().Single(item => Grid.GetColumn(item) == 1);
                Assert.True(label.Bounds.Width >= 120, $"'{label.Text}' only gets {label.Bounds.Width}");
                Assert.True(label.TranslatePoint(new Point(label.Bounds.Width, 0), row)!.Value.X <= control.Bounds.Left, $"'{label.Text}' overlaps its control");
                Assert.True(control.Bounds.Right <= row.Bounds.Width + 0.5, $"the control beside '{label.Text}' leaves its row");
            });
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Every tab of the production profile window keeps opaque, non-hovering reading panels.</summary>
    /// <param name="tab">The profile window tab to mount.</param>
    [AvaloniaTheory]
    [InlineData("content")]
    [InlineData("profile")]
    [InlineData("game")]
    public void ProfileWindowTabs_KeepOpaqueReadingPanels(string tab)
    {
        var viewModel = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ProfileTab();
        UserControl view = tab switch
        {
            "content" => new GameProfileContentEditorView { DataContext = viewModel },
            "profile" => new GameProfileGeneralSettingsView { DataContext = viewModel },
            _ => new GameSettingsView { DataContext = viewModel.GameSettingsViewModel },
        };
        var window = new Window { Width = 1200, Height = 900, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var cardFill = ResourceColor("CardBackground");
            var cardClass = tab == "content" ? "section-card" : "settings-card";
            var cards = view.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains(cardClass) && border.IsEffectivelyVisible).ToArray();
            Assert.NotEmpty(cards);
            Assert.All(cards, card => Assert.Equal(cardFill, BrushColor(card.Background)));
            Assert.Equal((byte)255, cardFill.A);

            if (tab != "content")
            {
                var card = cards.First(item => item.Bounds.Width > 0 && item.Bounds.Height > 0);
                card.Transitions = null;
                var restBorder = BrushColor(card.BorderBrush);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                window.MouseMove(card.TranslatePoint(new Point(card.Bounds.Width / 2, 4), window)!.Value);
                Dispatcher.UIThread.RunJobs();
                Assert.True(card.IsPointerOver);
                Assert.Equal(restBorder, BrushColor(card.BorderBrush));
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static Color ResourceColor(string name) => BrushColor((IBrush)Avalonia.Application.Current!.FindResource(name)!);

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
