using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Common.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Common.Controls;

/// <summary>
/// Unit tests for <see cref="SectionScrollSpy{TKey}"/>.
/// </summary>
public class SectionScrollSpyTests
{
    private sealed record ScrollSpyHost(
        Window Window,
        ScrollViewer ScrollViewer,
        Control First,
        Control Second,
        Control Third);

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
    /// Verifies that registering sections and attaching does not trigger an immediate spurious report.
    /// </summary>
    [AvaloniaFact]
    public void Attach_WithoutScroll_ReportsNothing()
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

    /// <summary>
    /// Verifies that when content expands dynamically, ScrollToControl tracks the target
    /// control rather than remaining clamped to the initial extent.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task ScrollToControl_DynamicExtent_TracksTargetControlAsync()
    {
        var host = CreateHost();
        try
        {
            using var spy = CreateAttachedSpy(host, new List<string>());

            spy.ScrollToControl(host.Third);
            Assert.True(spy.IsScrollingProgrammatically);

            // Dynamically expand second and third child so target position and extent expand
            host.Second.Height = 800;
            host.Third.Height = 800;
            Dispatcher.UIThread.RunJobs();

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && spy.IsScrollingProgrammatically)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(25);
            }

            Assert.False(spy.IsScrollingProgrammatically);

            var content = Assert.IsAssignableFrom<Control>(host.ScrollViewer.Content);
            var transform = host.Third.TransformToVisual(content);
            Assert.True(transform.HasValue);
            var position = transform.Value.Transform(new Point(0, 0));
            var maxScrollY = Math.Max(0, host.ScrollViewer.Extent.Height - host.ScrollViewer.Viewport.Height);
            var expected = Math.Clamp(position.Y, 0, maxScrollY);
            Assert.InRange(host.ScrollViewer.Offset.Y, expected - 2, expected + 2);
        }
        finally
        {
            host.Window.Close();
        }
    }

    /// <summary>
    /// Verifies that when animated scroll completes near or at the bottom boundary,
    /// the explicitly requested target section is preserved and reported rather than
    /// overwritten by bottom snapping to the last section.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task ScrollToSection_WhenLandingNearBottom_PreservesTargetSectionAsync()
    {
        var host = CreateBottomClampedHost();
        try
        {
            var reported = new List<string>();
            using var spy = CreateAttachedSpy(host, reported);

            spy.ScrollToSection("second");
            Assert.True(spy.IsScrollingProgrammatically);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && spy.IsScrollingProgrammatically)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(25);
            }

            Assert.False(spy.IsScrollingProgrammatically);
            Assert.Contains("second", reported);
            Assert.Equal("second", reported[^1]);
            Assert.DoesNotContain("third", reported);
        }
        finally
        {
            host.Window.Close();
        }
    }

    /// <summary>
    /// Verifies that scrolling to the section that is already at the current offset does not suppress
    /// subsequent user scroll notifications.
    /// </summary>
    [AvaloniaFact]
    public void ScrollToSection_WhenAlreadyAtTarget_DoesNotSuppressSubsequentUserScroll()
    {
        var host = CreateHost();
        try
        {
            var reported = new List<string>();
            using var spy = CreateAttachedSpy(host, reported);
            reported.Clear();

            // "first" section is already at offset Y = 0
            spy.ScrollToSection("first");
            Assert.False(spy.IsScrollingProgrammatically);

            // User scrolls to second section
            host.ScrollViewer.Offset = new Vector(0, 450);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { "first", "second" }, reported);
        }
        finally
        {
            host.Window.Close();
        }
    }

    /// <summary>
    /// Verifies that when an animated scroll completes, subsequent user scrolling
    /// is not suppressed.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task ScrollToSection_WhenAnimationCompletes_DoesNotSuppressSubsequentUserScrollAsync()
    {
        var host = CreateHost();
        try
        {
            var reported = new List<string>();
            using var spy = CreateAttachedSpy(host, reported);
            reported.Clear();

            // Animate scroll to "second" section
            spy.ScrollToSection("second");
            Assert.True(spy.IsScrollingProgrammatically);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline && spy.IsScrollingProgrammatically)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(25);
            }

            Assert.False(spy.IsScrollingProgrammatically);
            Assert.Equal("second", reported[^1]);
            reported.Clear();

            // Simulate genuine user scroll to "third" section
            host.ScrollViewer.Offset = new Vector(0, 850);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("third", reported);
        }
        finally
        {
            host.Window.Close();
        }
    }

    /// <summary>
    /// Verifies that when at the top of the scroll viewer, bottom catch-up does not
    /// prematurely report an unreachable bottom section.
    /// </summary>
    [AvaloniaFact]
    public void ScrollChanged_AtTop_WithUnreachableBottomSection_ReportsFirstSection()
    {
        var host = CreateBottomClampedHost();
        try
        {
            var reported = new List<string>();
            using var spy = CreateAttachedSpy(host, reported);

            host.ScrollViewer.Offset = new Vector(0, 50);
            Dispatcher.UIThread.RunJobs();
            reported.Clear();

            // Explicitly set offset back to 0 to trigger scroll event at the very top
            host.ScrollViewer.Offset = new Vector(0, 0);
            Dispatcher.UIThread.RunJobs();

            // When at the top, first section must be reported, never third
            Assert.Equal("first", Assert.Single(reported));
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

    private static ScrollSpyHost CreateBottomClampedHost()
    {
        var first = new Border { Height = 200 };
        var second = new Border { Height = 100 };
        var third = new Border { Height = 100 };

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
}
