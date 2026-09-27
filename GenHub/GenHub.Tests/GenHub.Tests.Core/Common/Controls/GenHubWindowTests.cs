using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GenHub.Common.Controls;
using System;
using Xunit;

namespace GenHub.Tests.Core.Common.Controls;

/// <summary>
/// Unit tests for <see cref="GenHubWindow"/>.
/// </summary>
public class GenHubWindowTests
{
    /// <summary>
    /// Verifies that opening a <see cref="GenHubWindow"/> applies platform decorations,
    /// overriding any explicit XAML value the same way XAML assignment would.
    /// </summary>
    [AvaloniaFact]
    public void Opened_AppliesPlatformDecorations()
    {
        var window = new GenHubWindow { SystemDecorations = SystemDecorations.Full };
        window.Show();

        Assert.IsAssignableFrom<Window>(window);
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(SystemDecorations.None, window.SystemDecorations);
        }
        else
        {
            Assert.Equal(SystemDecorations.Full, window.SystemDecorations);
        }

        window.Close();
    }
}
