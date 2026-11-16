using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Linq;

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

    /// <summary>
    /// Tag marker identifying the auto-generated resize grips overlay.
    /// </summary>
    private const string ResizeGripsMarker = "GenHubResizeGrips";

    /// <summary>
    /// Thickness of the auto-generated resize grip edges, matching the main window grip grid.
    /// </summary>
    private const int ResizeGripThickness = 6;

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

    /// <summary>
    /// Ensures a borderless resizable window has resize grips on Linux by overlaying a
    /// 6px edge grid on the window adorner layer. No-op on other platforms, for
    /// non-resizable windows, for windows with native decorations, and when no adorner
    /// layer is available. Safe to call multiple times.
    /// </summary>
    /// <param name="window">The window to equip with resize grips.</param>
    public static void EnsureResizeGrips(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!OperatingSystem.IsLinux() || !window.CanResize || window.SystemDecorations != SystemDecorations.None)
        {
            return;
        }

        var adornerLayer = FindAdornerLayer(window);
        if (adornerLayer?.Children is null)
        {
            return;
        }

        foreach (var child in adornerLayer.Children)
        {
            if (child is Control control && Equals(control.Tag, ResizeGripsMarker))
            {
                return;
            }
        }

        var grips = new Grid
        {
            Tag = ResizeGripsMarker,
            ColumnDefinitions = new ColumnDefinitions($"{ResizeGripThickness},*,{ResizeGripThickness}"),
            RowDefinitions = new RowDefinitions($"{ResizeGripThickness},*,{ResizeGripThickness}"),
            ZIndex = 99999,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };

        AddGrip(grips, WindowEdge.NorthWest, 0, 0);
        AddGrip(grips, WindowEdge.North, 0, 1);
        AddGrip(grips, WindowEdge.NorthEast, 0, 2);
        AddGrip(grips, WindowEdge.West, 1, 0);
        AddGrip(grips, WindowEdge.East, 1, 2);
        AddGrip(grips, WindowEdge.SouthWest, 2, 0);
        AddGrip(grips, WindowEdge.South, 2, 1);
        AddGrip(grips, WindowEdge.SouthEast, 2, 2);

        adornerLayer.Children.Add(grips);
        AttachResizeGrips(window, grips);
    }

    /// <summary>
    /// Resolves the adorner layer hosting the window content.
    /// <see cref="AdornerLayer.GetAdornerLayer"/> walks visual ancestors, so it
    /// always returns null for a <see cref="Window"/> (the visual root); the layer
    /// lives inside the window template below the root instead.
    /// </summary>
    /// <param name="window">The window whose adorner layer to resolve.</param>
    /// <returns>The adorner layer, or null when the window is not shown yet.</returns>
    private static AdornerLayer? FindAdornerLayer(Window window)
    {
        if ((window.Content as Visual) is { } content)
        {
            var fromContent = AdornerLayer.GetAdornerLayer(content);
            if (fromContent is not null)
            {
                return fromContent;
            }
        }

        return window.GetVisualDescendants().OfType<AdornerLayer>().FirstOrDefault();
    }

    private static void OnAdaptForPlatformChanged(Window window, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            ApplyPlatformDecorations(window);
        }
    }

    private static void AddGrip(Grid grips, WindowEdge edge, int row, int column)
    {
        var border = new Border { Tag = edge };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        grips.Children.Add(border);
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
