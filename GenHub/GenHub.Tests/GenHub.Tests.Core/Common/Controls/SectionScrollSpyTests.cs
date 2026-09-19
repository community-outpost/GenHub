using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Common.Controls;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Common.Controls;

/// <summary>
/// Verifies sidebar scroll-spy section detection shared by settings-style pages.
/// </summary>
public class SectionScrollSpyTests
{
    private sealed record ScrollSpyHost(Window Window, ScrollViewer ScrollViewer, Border First, Border Second, Border Third);

    /// <summary>
    /// Verifies that scrolling reports the last section whose top is above the visibility threshold.
    /// </summary>
    [AvaloniaFact]
    public void ScrollChanged_ReportsTopmostVisibleSection()
    {
        var host = CreateHost();
        try
        {
            var reported = new List<string>();
            using var spy = CreateAttachedSpy(host, reported);

            host.ScrollViewer.Offset = new Vector(host.ScrollViewer.Offset.X, 450);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("second", Assert.Single(reported));
        }
        finally
        {
            host.Window.Close();
        }
    }

    /// <summary>
    /// Verifies that scrolling to the bottom reports the last section.
    /// </summary>
    [AvaloniaFact]
    public void ScrollChanged_AtBottom_ReportsLastSection()
    {
        var host = CreateHost();
        try
        {
            var reported = new List<string>();
            using var spy = CreateAttachedSpy(host, reported);

            var maxScrollY = host.ScrollViewer.Extent.Height - host.ScrollViewer.Viewport.Height;
            Assert.True(maxScrollY > 0);
            host.ScrollViewer.Offset = new Vector(host.ScrollViewer.Offset.X, maxScrollY);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("third", Assert.Single(reported));
        }
        finally
        {
            host.Window.Close();
        }
    }

    /// <summary>
    /// Verifies that programmatic scroll starts only for registered sections.
    /// </summary>
    [AvaloniaFact]
    public void ScrollToSection_ResolvesRegisteredSectionsOnly()
    {
        var host = CreateHost();
        try
        {
            using var spy = CreateAttachedSpy(host, new List<string>());

            spy.ScrollToSection("unknown");
            Assert.False(spy.IsScrollingProgrammatically);

            spy.ScrollToSection("second");
            Assert.True(spy.IsScrollingProgrammatically);
        }
        finally
        {
            host.Window.Close();
        }
    }

    private static ScrollSpyHost CreateHost()
    {
        var first = new Border { Height = 400 };
        var second = new Border { Height = 400 };
        var third = new Border { Height = 400 };
        var scrollViewer = new ScrollViewer
        {
            Content = new StackPanel
            {
                Children = { first, second, third },
            },
        };
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Content = scrollViewer,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        return new ScrollSpyHost(window, scrollViewer, first, second, third);
    }

    private static SectionScrollSpy<string> CreateAttachedSpy(ScrollSpyHost host, List<string> reported)
    {
        var spy = new SectionScrollSpy<string>(host.ScrollViewer, reported.Add);
        spy.RegisterSection("first", host.First);
        spy.RegisterSection("second", host.Second);
        spy.RegisterSection("third", host.Third);
        spy.Attach();

        return spy;
    }
}
