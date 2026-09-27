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
    /// Verifies that constructing a <see cref="GenHubWindow"/> applies platform decorations
    /// without throwing and yields a usable <see cref="Window"/>.
    /// </summary>
    [AvaloniaFact]
    public void Constructor_AppliesPlatformDecorations()
    {
        var window = new GenHubWindow();

        Assert.IsAssignableFrom<Window>(window);
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(SystemDecorations.None, window.SystemDecorations);
        }
        else
        {
            Assert.Equal(SystemDecorations.Full, window.SystemDecorations);
        }
    }
}
