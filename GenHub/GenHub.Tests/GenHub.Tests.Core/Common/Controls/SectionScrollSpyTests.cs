using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Common.Controls;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Common.Controls;

/// <summary>
/// Unit tests for <see cref="SectionScrollSpy{TKey}"/>.
/// </summary>
public class SectionScrollSpyTests
{
    /// <summary>
    /// Verifies that registering sections adds them without immediate scroll activation.
    /// </summary>
    [AvaloniaFact]
    public void RegisterSection_AddsSectionsInOrder()
    {
        var host = CreateHost();
        try
        {
            var reported = new List<string>();
            using var spy = CreateAttachedSpy(host, reported);

            Assert.Empty(reported);
        }
        finally
        {
            host.Window.Close();
        }
    }

    /// <summary>
    /// Verifies that scrolling to an existing section starts programmatic scroll mode.
    /// </summary>
    [AvaloniaFact]
    public void ScrollToSection_StartsProgrammaticScroll()
    {
        var host = CreateHost();
        try
        {
            using var spy = CreateAttachedSpy(host, new List<string>());

            spy.ScrollToSection("second");
            Assert.True(spy.IsScrollingProgrammatically);
        }
        finally
        {
            host.Window.Close();
        }
    }

    /// <summary>
    /// Verifies that scrolling to an unknown key does not start programmatic scroll.
    /// </summary>
    [AvaloniaFact]
    public void ScrollToSection_UnknownKey_DoesNotStartProgrammaticScroll()
    {
        var host = CreateHost();
        try
        {
            using var spy = CreateAttachedSpy(host, new List<string>());

            spy.ScrollToSection("unknown");
            Assert.False(spy.IsScrollingProgrammatically);
        }
        finally
        {
            host.Window.Close();
        }
    }

    /// <summary>
    /// Verifies that disposing the scroll spy detaches event handlers and stops tracking.
    /// </summary>
    [AvaloniaFact]
    public void Dispose_DetachesAndStopsScrollTracking()
    {
        var host = CreateHost();
        try
        {
            var reported = new List<string>();
            var spy = CreateAttachedSpy(host, reported);

            spy.ScrollToSection("second");
            spy.Dispose();

            Assert.False(spy.IsScrollingProgrammatically);
            host.ScrollViewer.Offset = new Vector(0, 100);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(reported);
        }
        finally
        {
            host.Window.Close();
        }
    }

    /// <summary>
    /// Verifies that programmatic scroll starts when scrolling directly to a control.
    /// </summary>
    [AvaloniaFact]
    public void ScrollToControl_StartsProgrammaticScroll()
    {
        var host = CreateHost();
        try
        {
            using var spy = CreateAttachedSpy(host, new List<string>());

            spy.ScrollToControl(host.Second);
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

        var content = new StackPanel();
        content.Children.Add(first);
        content.Children.Add(second);
        content.Children.Add(third);

        var scrollViewer = new ScrollViewer
        {
            Height = 300,
            Content = content,
        };

        var window = new Window
        {
            Width = 600,
            Height = 400,
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

    private sealed record ScrollSpyHost(
        Window Window,
        ScrollViewer ScrollViewer,
        Control First,
        Control Second,
        Control Third);
}
