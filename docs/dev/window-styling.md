---
title: Window Styling and OS Animation Standards
description: Guidelines and architectural rules for Avalonia window configuration, custom title bars, and native OS maximize/restore animations in GenHub
---

# Window Styling & OS Animation Standards

This document establishes the mandatory standards for creating and configuring `Window` instances in GenHub. Following these patterns ensures that all windows achieve smooth, native OS animations (such as Desktop Window Manager / DWM fluid maximize, restore, snap, and dragging transitions) without clunkiness or visual glitches.

---

## 1. The Core Architecture: Native DWM Integration

Avalonia runs cross-platform across Windows, Linux, and macOS. On Windows (Win32), the operating system's **Desktop Window Manager (DWM)** manages fluid maximize/restore zoom animations, Aero Snap, and window shadows.

For DWM to provide native fluid animations on windows with custom-styled title bars, the window **MUST** retain its native top-level frame (`WS_OVERLAPPEDWINDOW`) while extending its client area over the OS chrome.

### Mandatory Window XAML Properties

All resizable windows with custom title bars in GenHub must define these attributes:

```xml
<Window xmlns="https://github.com/avaloniaui"
        ...
        CanResize="True"
        SystemDecorations="Full"
        ExtendClientAreaToDecorationsHint="True"
        ExtendClientAreaChromeHints="NoChrome"
        ExtendClientAreaTitleBarHeightHint="-1">
```

### Why Each Property Matters

| Property | Value | Purpose | Why It Fails Without It |
|---|---|---|---|
| `SystemDecorations` | `"Full"` | Retains top-level OS window styles (`WS_CAPTION`, `WS_THICKFRAME`, `WS_MAXIMIZEBOX`). | Setting `"BorderOnly"` or `"None"` strips maximize styles, causing DWM to disable maximize/restore animations and snap instantly. |
| `ExtendClientAreaToDecorationsHint` | `"True"` | Extends the application XAML drawing surface across the entire window. | Without it, the OS renders a standard generic white/grey caption bar above the content. |
| `ExtendClientAreaChromeHints` | `"NoChrome"` | Hides the default OS minimize, maximize, and close caption buttons. | Without it, default OS caption buttons clash with custom UI buttons. |
| `ExtendClientAreaTitleBarHeightHint` | `"-1"` | Instructs Avalonia to remove default title bar reservation space. | Ensures full control of header height via XAML. |

---

## 2. Standard Title Bar Interaction Pattern

### XAML Header Definition

The header area should be an interactive container (`Grid` or `Border`) with a transparent background that captures pointer events:

```xml
<!-- Custom Title Bar Header Area -->
<Grid Grid.Row="0"
      Background="Transparent"
      PointerPressed="OnTitleBarPointerPressed">
    <!-- Header Content: Icons, Title, Actions, Window Controls -->
</Grid>
```

> [!IMPORTANT]
> Never set `IsHitTestVisible="False"` on the drag area container, or pointer events cannot be captured for dragging or double-click maximizing.

### Code-Behind Handler

The code-behind must implement pointer dragging and double-click maximizing using Avalonia's built-in `BeginMoveDrag`:

```csharp
/// <summary>
/// Handles pointer pressed events on the title bar for dragging and maximizing.
/// </summary>
/// <param name="sender">The sender object.</param>
/// <param name="e">The pointer event arguments.</param>
private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
{
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
    {
        if (e.ClickCount == 2 && CanResize)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            BeginMoveDrag(e);
        }
    }
}

/// <summary>
/// Handles the maximize/restore button click.
/// </summary>
/// <param name="sender">The sender object.</param>
/// <param name="e">The routed event arguments.</param>
private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
{
    WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
```

---

## 3. Strict Rules & Anti-Patterns (For Agents & Developers)

> [!CAUTION]
> **NEVER MANUALLY TRACK MOUSE MOVES OR MANUALLY UNMAXIMIZE DURING DRAG**
> 
> A common anti-pattern is writing manual `PointerMoved` tracking with a pixel distance threshold, manually setting `WindowState = WindowState.Normal`, calculating pixel coordinates, and setting `Position = new PixelPoint(...)`.
> 
> **Why this breaks:**
> 1. It bypasses DWM's native interactive unmaximize animation.
> 2. It causes the window to jarringly jump/teleport on screen.
> 3. It breaks mouse capture and makes window dragging feel laggy and disconnected.
> 
> **Solution:** Always call `BeginMoveDrag(e)` directly on pointer press. Avalonia and the OS window manager will handle dragging off maximized state smoothly.

> [!CAUTION]
> **NEVER USE `SystemDecorations="BorderOnly"` ON RESIZABLE/MAXIMIZABLE WINDOWS**
> 
> Setting `BorderOnly` disables DWM maximize/restore zoom transitions. Always use `SystemDecorations="Full"` combined with `ExtendClientArea*`.

---

## 4. Window Types Reference in GenHub

| Window Class | Role | `SystemDecorations` | `CanResize` | Custom Title Bar Drag |
|---|---|---|---|---|
| `MainWindow` | Primary application shell | `Full` | `True` | `OnTitleBarPointerPressed` |
| `GameProfileSettingsWindow` | Profile configuration editor | `Full` | `True` | `OnHeaderPointerPressed` |
| `UpdateNotificationWindow` | Velopack update dialog | `Full` | `True` | `TitleBar_PointerPressed` |
| `AddLocalContentWindow` | Content importer dialog | `Full` | `True` | `OnTitleBarPointerPressed` |
| `GenericMessageWindow` | Modal message/announcement dialog | `None` | `False` | Modal centered |
| `ConfirmationDialogWindow` | Modal confirmation dialog | `None` | `False` | Modal centered |
| `UpdateOptionDialogWindow` | Modal update option dialog | `None` | `False` | Modal centered |
| `SetupWizardView` | First-run wizard dialog | `None` | `False` | Modal centered |
| `GitHubTokenDialogView` | GitHub PAT configuration dialog | `BorderOnly` | `False` | Modal centered |

---

## 5. Checklist for New Windows

When creating a new `Window` in GenHub:

- [ ] Set `SystemDecorations="Full"` if the window can be resized or maximized.
- [ ] Set `ExtendClientAreaToDecorationsHint="True"`, `ExtendClientAreaChromeHints="NoChrome"`, and `ExtendClientAreaTitleBarHeightHint="-1"`.
- [ ] Implement `OnTitleBarPointerPressed` with `BeginMoveDrag(e)` and double-click maximize toggle.
- [ ] Ensure the drag container has `Background="Transparent"` and `IsHitTestVisible="True"`.
- [ ] Avoid manual coordinate calculation or custom drag threshold tracking.
- [ ] Adhere to code style: no `this.`, primary constructors where applicable, no mid-comment capitalization.
