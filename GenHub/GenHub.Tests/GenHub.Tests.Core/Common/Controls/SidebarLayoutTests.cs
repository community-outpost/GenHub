using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Common.Controls;
using Xunit;

namespace GenHub.Tests.Core.Common.Controls;

/// <summary>
/// Unit tests for <see cref="SidebarLayout"/>.
/// </summary>
public class SidebarLayoutTests
{
    /// <summary>
    /// Verifies that default property values are correctly initialized.
    /// </summary>
    [Fact]
    public void Properties_DefaultValues_AreCorrect()
    {
        var layout = new SidebarLayout();

        Assert.True(layout.IsPaneOpen);
        Assert.Equal(Dock.Left, layout.PanePlacement);
        Assert.Equal("Sections", layout.PaneTitle);
        Assert.Equal(220, layout.OpenPaneLength);
        Assert.Equal(140, layout.MinPaneLength);
        Assert.Equal(360, layout.MaxPaneLength);
    }

    /// <summary>
    /// Verifies that pane placement can be set to Dock.Right.
    /// </summary>
    [Fact]
    public void PanePlacement_CanBeSetToRight()
    {
        var layout = new SidebarLayout
        {
            PanePlacement = Dock.Right,
        };

        Assert.Equal(Dock.Right, layout.PanePlacement);
    }

    /// <summary>
    /// Verifies that pane toggle commands update IsPaneOpen state.
    /// </summary>
    [Fact]
    public void Commands_ToggleOpenAndClosePane()
    {
        var layout = new SidebarLayout();
        Assert.True(layout.IsPaneOpen);

        layout.ClosePaneCommand.Execute(null);
        Assert.False(layout.IsPaneOpen);

        layout.OpenPaneCommand.Execute(null);
        Assert.True(layout.IsPaneOpen);

        layout.TogglePaneCommand.Execute(null);
        Assert.False(layout.IsPaneOpen);

        layout.TogglePaneCommand.Execute(null);
        Assert.True(layout.IsPaneOpen);
    }

    /// <summary>
    /// Verifies that GetSanitizedBounds resolves invalid bounds safely.
    /// </summary>
    /// <param name="min">The proposed minimum pane length.</param>
    /// <param name="max">The proposed maximum pane length.</param>
    /// <param name="expectedMin">The expected resolved minimum pane length.</param>
    /// <param name="expectedMax">The expected resolved maximum pane length.</param>
    [Theory]
    [InlineData(double.NaN, double.NaN, 140, 360)]
    [InlineData(-10, -50, 140, 360)]
    [InlineData(100, 50, 100, 360)]
    [InlineData(200, 500, 200, 500)]
    public void GetSanitizedBounds_ResolvesCorrectLimits(double min, double max, double expectedMin, double expectedMax)
    {
        var (resolvedMin, resolvedMax) = SidebarLayout.GetSanitizedBounds(min, max);

        Assert.Equal(expectedMin, resolvedMin);
        Assert.Equal(expectedMax, resolvedMax);
    }

    /// <summary>
    /// Verifies that SidebarLayout can be attached to visual tree with Right placement.
    /// </summary>
    [AvaloniaFact]
    public void SidebarLayout_WithRightPlacement_AttachesCleanly()
    {
        var layout = new SidebarLayout
        {
            PanePlacement = Dock.Right,
            PaneTitle = "Test Right Pane",
            Content = new TextBlock { Text = "Main Content" },
        };

        var window = new Window
        {
            Width = 800,
            Height = 600,
            Content = layout,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(layout.IsPaneOpen);
            Assert.Equal(Dock.Right, layout.PanePlacement);
        }
        finally
        {
            window.Close();
        }
    }
}
