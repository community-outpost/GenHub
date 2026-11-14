using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Features.Tools.MapManager.Views;
using GenHub.Features.Tools.ReplayManager.Views;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Views;

/// <summary>Verifies shared split actions in their actual tool view consumers.</summary>
public class SplitActionStyleTests
{
    /// <summary>The gradient spans both independent hit targets instead of restarting at the arrow.</summary>
    /// <param name="replays">Whether to inspect the replay manager instead of the map manager.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UploadCapsule_OwnsOneContinuousGradient(bool replays)
    {
        UserControl view = replays ? new ReplayManagerView() : new MapManagerView();
        var window = new Window { Width = 1100, Height = 760, Content = view };
        window.Show();
        try
        {
            var gameTabs = view.GetVisualDescendants().OfType<RadioButton>().Where(button => button.Classes.Contains("TabButton")).ToArray();
            Assert.Equal(2, gameTabs.Length);
            foreach (var tab in gameTabs)
            {
                var tabBorder = tab.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Root");
                Assert.Equal(new Avalonia.CornerRadius(999), tabBorder.CornerRadius);
            }

            var capsule = Assert.Single(view.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("split-action"));
            Assert.IsAssignableFrom<ILinearGradientBrush>(capsule.Background);
            var left = Assert.Single(capsule.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("SplitLeft"));
            var right = Assert.Single(capsule.GetVisualDescendants().OfType<ToggleButton>(), button => button.Classes.Contains("SplitRight"));
            Assert.Same(left.Parent, right.Parent);

            // No view model is attached: isolate the enabled visual state from command availability.
            foreach (var control in new Avalonia.Controls.Primitives.TemplatedControl[] { left, right })
            {
                control.GetVisualDescendants().OfType<ContentPresenter>().First(item => item.Name == "PART_ContentPresenter").Transitions = null;
            }

            left.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            left.IsEnabled = true;
            right.IsEnabled = true;
            Dispatcher.UIThread.RunJobs();
            foreach (var control in new Avalonia.Controls.Primitives.TemplatedControl[] { left, right })
            {
                Assert.True(control.IsEffectivelyEnabled);
                var presenter = control.GetVisualDescendants().OfType<ContentPresenter>().First(item => item.Name == "PART_ContentPresenter");
                Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
            }

            var arrowPresenter = right.GetVisualDescendants().OfType<ContentPresenter>().First(item => item.Name == "PART_ContentPresenter");
            Assert.Equal(new Avalonia.Thickness(1, 0, 0, 0), arrowPresenter.BorderThickness);
            Assert.Equal(new Avalonia.CornerRadius(999), capsule.CornerRadius);
            right.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.IsAssignableFrom<ILinearGradientBrush>(capsule.Background);
            Assert.True(Assert.IsAssignableFrom<ISolidColorBrush>(arrowPresenter.Background).Color.A < 255);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A checked history toggle must still mute its glyph when disabled.</summary>
    /// <param name="replays">Whether to inspect the replay manager instead of the map manager.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckedSplitAction_UsesDisabledInk(bool replays)
    {
        UserControl view = replays ? new ReplayManagerView() : new MapManagerView();
        var window = new Window { Width = 1100, Height = 760, Content = view };
        window.Show();
        try
        {
            var toggle = Assert.Single(view.GetVisualDescendants().OfType<ToggleButton>(), button => button.Classes.Contains("SplitRight"));
            toggle.IsChecked = true;
            toggle.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            var glyph = Assert.IsType<PathIcon>(toggle.Content);
            var expected = Assert.IsAssignableFrom<ISolidColorBrush>(Avalonia.Application.Current!.FindResource("ActionDisabledInk")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(toggle.Foreground).Color);
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color);
            Assert.Equal(new Avalonia.CornerRadius(0, 999, 999, 0), toggle.CornerRadius);
        }
        finally
        {
            window.Close();
        }
    }
}
