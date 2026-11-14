using Avalonia.Controls;
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
