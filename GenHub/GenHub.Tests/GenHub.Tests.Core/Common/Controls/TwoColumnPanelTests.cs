using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FluentAssertions;
using GenHub.Common.Controls;
using Xunit;

namespace GenHub.Tests.Core.Common.Controls;

/// <summary>
/// Layout tests for the responsive <see cref="TwoColumnPanel"/> flex grid.
/// </summary>
public sealed class TwoColumnPanelTests
{
    /// <summary>
    /// Verifies that a wide panel arranges children in two columns.
    /// </summary>
    [AvaloniaFact]
    public void WidePanel_ArrangesTwoColumns()
    {
        var panel = new TwoColumnPanel { Width = 500 };
        AddCards(panel, 4);
        var window = new Window { Width = 600, Height = 400, Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            var first = panel.Children[0].Bounds;
            var second = panel.Children[1].Bounds;
            var third = panel.Children[2].Bounds;
            first.X.Should().BeApproximately(0, 0.5);
            second.X.Should().BeGreaterThan(first.X);
            third.X.Should().BeApproximately(first.X, 0.5);
            third.Y.Should().BeGreaterThan(first.Y);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that a narrow panel collapses to a single column instead of squeezing cards.
    /// </summary>
    [AvaloniaFact]
    public void NarrowPanel_CollapsesToSingleColumn()
    {
        var panel = new TwoColumnPanel { Width = 200 };
        AddCards(panel, 3);
        var window = new Window { Width = 300, Height = 400, Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            foreach (var child in panel.Children)
            {
                child.Bounds.X.Should().BeApproximately(0, 0.5);
            }

            panel.Children[1].Bounds.Y.Should().BeGreaterThan(panel.Children[0].Bounds.Y);
            panel.Children[2].Bounds.Y.Should().BeGreaterThan(panel.Children[1].Bounds.Y);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that the column count honors the configured maximum on wide layouts.
    /// </summary>
    [AvaloniaFact]
    public void WidePanelWithMaxColumns_SpreadsAcrossAllColumns()
    {
        var panel = new TwoColumnPanel { Width = 1100, MaxColumns = 4 };
        AddCards(panel, 4);
        var window = new Window { Width = 1200, Height = 400, Content = panel };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            double previous = -1;
            foreach (var child in panel.Children)
            {
                child.Bounds.X.Should().BeGreaterThan(previous);
                child.Bounds.Y.Should().BeApproximately(0, 0.5);
                previous = child.Bounds.X;
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void AddCards(TwoColumnPanel panel, int count)
    {
        for (int i = 0; i < count; i++)
        {
            panel.Children.Add(new Border { Height = 20 });
        }
    }
}
