using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;

namespace GenHub.Common.Helpers;

/// <summary>
/// Helper class for managing cross-platform window chrome and decorations.
/// </summary>
public static class WindowChromeHelper
{
    /// <summary>
    /// Defines the <see cref="AdaptForPlatformProperty"/> attached property.
    /// When set to <c>true</c>, adjusts window decorations based on the operating system.
    /// </summary>
    public static readonly AttachedProperty<bool> AdaptForPlatformProperty =
        AvaloniaProperty.RegisterAttached<Window, bool>(
            "AdaptForPlatform",
            typeof(WindowChromeHelper),
            defaultValue: false);

    static WindowChromeHelper()
    {
        AdaptForPlatformProperty.Changed.AddClassHandler<Window>(OnAdaptForPlatformChanged);
    }

    /// <summary>
    /// Gets the value of the <see cref="AdaptForPlatformProperty"/> attached property.
    /// </summary>
    /// <param name="element">The target window.</param>
    /// <returns>True if the window should adapt decorations for the platform; otherwise, false.</returns>
    public static bool GetAdaptForPlatform(Window element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(AdaptForPlatformProperty);
    }

    /// <summary>
    /// Sets the value of the <see cref="AdaptForPlatformProperty"/> attached property.
    /// </summary>
    /// <param name="element">The target window.</param>
    /// <param name="value">True to adapt decorations for the platform.</param>
    public static void SetAdaptForPlatform(Window element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(AdaptForPlatformProperty, value);
    }

    /// <summary>
    /// Applies platform-specific window decorations to prevent duplicate title bars on Linux
    /// while preserving native Windows Snap layouts, shadows, and macOS chrome.
    /// </summary>
    /// <param name="window">The window to adjust.</param>
    public static void ApplyPlatformDecorations(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (OperatingSystem.IsLinux())
        {
            // On Linux X11/Wayland desktop environments, window managers draw Server-Side Decorations (SSD)
            // if SystemDecorations is Full or BorderOnly. Because Linux window managers do not support
            // extending the client area into server decorations (ExtendClientAreaToDecorationsHint),
            // setting SystemDecorations to None eliminates duplicate title bars and rectangular outer frames.
            window.SystemDecorations = SystemDecorations.None;
        }
    }

    /// <summary>
    /// Configures edge and corner resize grip controls on a panel for Linux environments
    /// when <see cref="SystemDecorations.None"/> is active.
    /// </summary>
    /// <param name="window">The target window to resize.</param>
    /// <param name="gripsPanel">The panel containing border/edge controls tagged with <see cref="WindowEdge"/> names.</param>
    public static void AttachResizeGrips(Window window, Panel gripsPanel)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(gripsPanel);

        if (!OperatingSystem.IsLinux() || !window.CanResize)
        {
            gripsPanel.IsVisible = false;
            return;
        }

        gripsPanel.IsVisible = window.WindowState != WindowState.Maximized;
        window.GetObservable(Window.WindowStateProperty).Subscribe(state =>
        {
            gripsPanel.IsVisible = state != WindowState.Maximized;
        });

        foreach (var child in gripsPanel.Children)
        {
            if (child is not Control control)
            {
                continue;
            }

            WindowEdge? edge = control.Tag switch
            {
                WindowEdge enumEdge => enumEdge,
                string strEdge when Enum.TryParse<WindowEdge>(strEdge, out var parsed) => parsed,
                _ => null,
            };

            if (edge is null)
            {
                continue;
            }

            control.Cursor ??= GetCursorForEdge(edge.Value);
            if (control is Border border && border.Background is null)
            {
                border.Background = Brushes.Transparent;
            }

            var currentEdge = edge.Value;
            control.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
                {
                    window.BeginResizeDrag(currentEdge, e);
                }
            };
        }
    }

    private static void OnAdaptForPlatformChanged(Window window, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            ApplyPlatformDecorations(window);
        }
    }

    private static Cursor GetCursorForEdge(WindowEdge edge) => edge switch
    {
        WindowEdge.North => new Cursor(StandardCursorType.TopSide),
        WindowEdge.South => new Cursor(StandardCursorType.BottomSide),
        WindowEdge.West => new Cursor(StandardCursorType.LeftSide),
        WindowEdge.East => new Cursor(StandardCursorType.RightSide),
        WindowEdge.NorthWest => new Cursor(StandardCursorType.TopLeftCorner),
        WindowEdge.NorthEast => new Cursor(StandardCursorType.TopRightCorner),
        WindowEdge.SouthWest => new Cursor(StandardCursorType.BottomLeftCorner),
        WindowEdge.SouthEast => new Cursor(StandardCursorType.BottomRightCorner),
        _ => Cursor.Default,
    };
}
