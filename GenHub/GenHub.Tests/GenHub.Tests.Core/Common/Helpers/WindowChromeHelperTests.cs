using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GenHub.Common.Helpers;
using System;
using Xunit;

namespace GenHub.Tests.Core.Common.Helpers;

/// <summary>
/// Unit tests for <see cref="WindowChromeHelper"/>.
/// </summary>
public class WindowChromeHelperTests
{
    /// <summary>
    /// Verifies that <see cref="WindowChromeHelper.ApplyPlatformDecorations"/> throws an <see cref="ArgumentNullException"/>
    /// when passed a null window.
    /// </summary>
    [Fact]
    public void ApplyPlatformDecorations_ThrowsArgumentNullException_WhenWindowIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => WindowChromeHelper.ApplyPlatformDecorations(null!));
    }

    /// <summary>
    /// Verifies that <see cref="WindowChromeHelper.GetAdaptForPlatform"/> throws an <see cref="ArgumentNullException"/>
    /// when passed a null window.
    /// </summary>
    [Fact]
    public void GetAdaptForPlatform_ThrowsArgumentNullException_WhenWindowIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => WindowChromeHelper.GetAdaptForPlatform(null!));
    }

    /// <summary>
    /// Verifies that <see cref="WindowChromeHelper.SetAdaptForPlatform"/> throws an <see cref="ArgumentNullException"/>
    /// when passed a null window.
    /// </summary>
    [Fact]
    public void SetAdaptForPlatform_ThrowsArgumentNullException_WhenWindowIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => WindowChromeHelper.SetAdaptForPlatform(null!, true));
    }

    /// <summary>
    /// Verifies that <see cref="WindowChromeHelper.AttachResizeGrips"/> throws an <see cref="ArgumentNullException"/>
    /// when either parameter is null.
    /// </summary>
    [AvaloniaFact]
    public void AttachResizeGrips_ThrowsArgumentNullException_WhenArgumentsNull()
    {
        Assert.Throws<ArgumentNullException>(() => WindowChromeHelper.AttachResizeGrips(null!, new Panel()));
        Assert.Throws<ArgumentNullException>(() => WindowChromeHelper.AttachResizeGrips(new Window(), null!));
    }

    /// <summary>
    /// Verifies that the attached property <see cref="WindowChromeHelper.AdaptForPlatformProperty"/>
    /// can be set and retrieved accurately.
    /// </summary>
    [AvaloniaFact]
    public void AdaptForPlatform_AttachedProperty_CanBeSetAndRetrieved()
    {
        var window = new Window();
        Assert.False(WindowChromeHelper.GetAdaptForPlatform(window));

        WindowChromeHelper.SetAdaptForPlatform(window, true);
        Assert.True(WindowChromeHelper.GetAdaptForPlatform(window));

        WindowChromeHelper.SetAdaptForPlatform(window, false);
        Assert.False(WindowChromeHelper.GetAdaptForPlatform(window));
    }

    /// <summary>
    /// Verifies that <see cref="WindowChromeHelper.ApplyPlatformDecorations"/> applies without throwing.
    /// On Linux it sets SystemDecorations to None; on Windows/macOS it preserves existing decorations.
    /// </summary>
    [AvaloniaFact]
    public void ApplyPlatformDecorations_AppliesCorrectDecorationsForPlatform()
    {
        var window = new Window
        {
            SystemDecorations = SystemDecorations.Full,
        };

        WindowChromeHelper.ApplyPlatformDecorations(window);

        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(SystemDecorations.None, window.SystemDecorations);
        }
        else
        {
            Assert.Equal(SystemDecorations.Full, window.SystemDecorations);
        }
    }

    /// <summary>
    /// Verifies that <see cref="WindowChromeHelper.AttachResizeGrips"/> sets grips visibility to false
    /// when the window is non-resizable.
    /// </summary>
    [AvaloniaFact]
    public void AttachResizeGrips_HidesGrips_WhenWindowCannotResize()
    {
        var window = new Window
        {
            CanResize = false,
        };
        var panel = new Panel
        {
            IsVisible = true,
        };

        WindowChromeHelper.AttachResizeGrips(window, panel);
        Assert.False(panel.IsVisible);
    }
}
