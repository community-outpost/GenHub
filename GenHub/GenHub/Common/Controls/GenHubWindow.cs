using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GenHub.Common.Helpers;
using GenHub.Common.ViewModels;
using System;

namespace GenHub.Common.Controls;

/// <summary>
/// Base window for GenHub dialogs and windows. Applies platform window decorations
/// on open (XAML assignment runs after the base constructor and would otherwise
/// overwrite a construction-time value), equips borderless resizable windows with
/// resize grips on Linux, keeps windows within the working area, and centralizes
/// the title-bar drag, chrome button, and Escape-to-close handling so windows do
/// not repeat the chrome boilerplate.
/// </summary>
public class GenHubWindow : Window
{
    private IRequestCloseViewModel? _wiredViewModel;

    /// <summary>
    /// Gets a value indicating whether pressing Escape closes the window.
    /// Override to <c>false</c> for windows that must stay open, such as the main window.
    /// </summary>
    protected virtual bool CloseOnEscape => true;

    /// <summary>
    /// Gets a value indicating whether the window is fitted to the working area on open
    /// and kept within the screen on resize. Override to <c>false</c> for windows with
    /// their own persisted placement logic.
    /// </summary>
    protected virtual bool FitToScreenOnOpen => true;

    /// <summary>
    /// Gets a value indicating whether a disposable data context is disposed when the
    /// window closes. Opt in for windows whose view model subscribes to singleton services.
    /// </summary>
    protected virtual bool DisposeDataContextOnClose => false;

    /// <summary>
    /// Gets a value indicating whether double-clicking the title bar toggles maximized
    /// state. Override to <c>false</c> for fixed-purpose dialogs that should only drag.
    /// </summary>
    protected virtual bool TitleBarDoubleClickMaximizes => true;

    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WindowChromeHelper.ApplyPlatformDecorations(this);
        WindowChromeHelper.EnsureResizeGrips(this);
        if (FitToScreenOnOpen)
        {
            FitToScreen();
        }
    }

    /// <inheritdoc/>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (FitToScreenOnOpen)
        {
            EnsurePositionWithinScreen();
        }
    }

    /// <inheritdoc/>
    /// <param name="e">The key event arguments.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (CloseOnEscape && e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            OnEscapePressed();
        }
    }

    /// <summary>
    /// Runs when Escape is pressed and <see cref="CloseOnEscape"/> is enabled.
    /// Override to return a dialog result or run cleanup instead of closing.
    /// </summary>
    protected virtual void OnEscapePressed() => Close();

    /// <inheritdoc/>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UnwireRequestClose();
        WireRequestClose();
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        UnwireRequestClose();
        if (DisposeDataContextOnClose && DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// Handles pointer presses on the custom title bar: drags the window, and
    /// toggles maximized state on double-click for resizable windows.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The pointer event arguments.</param>
    protected virtual void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2 && CanResize && TitleBarDoubleClickMaximizes)
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
    /// Minimizes the window from the title-bar minimize button.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    protected virtual void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>
    /// Toggles the maximized state from the title-bar maximize button.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    protected virtual void MaximizeButton_Click(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>
    /// Closes the window from the title-bar close button.
    /// </summary>
    /// <param name="sender">The event sender.</param>
    /// <param name="e">The event arguments.</param>
    protected virtual void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void FitToScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null)
        {
            return;
        }

        var workingArea = screen.WorkingArea;
        var scaling = screen.Scaling;

        var availableWidth = workingArea.Width / scaling;
        var availableHeight = workingArea.Height / scaling;

        var maxDipsWidth = availableWidth * 0.90;
        var maxDipsHeight = availableHeight * 0.88;

        var widthCap = Math.Min(Math.Max(Math.Max(MinWidth, 400), maxDipsWidth), availableWidth);
        var heightCap = Math.Min(Math.Max(Math.Max(MinHeight, 300), maxDipsHeight), availableHeight);

        if (MaxWidth > widthCap)
        {
            MaxWidth = widthCap;
        }

        if (MaxHeight > heightCap)
        {
            MaxHeight = heightCap;
        }

        EnsurePositionWithinScreen();
    }

    private void EnsurePositionWithinScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null)
        {
            return;
        }

        var workingArea = screen.WorkingArea;
        var scaling = screen.Scaling;

        var screenLeft = workingArea.X;
        var screenTop = workingArea.Y;
        var screenRight = screenLeft + workingArea.Width;
        var screenBottom = screenTop + workingArea.Height;

        var windowWidth = (int)(Bounds.Width * scaling);
        var windowHeight = (int)(Bounds.Height * scaling);

        if (windowWidth <= 0 || windowHeight <= 0)
        {
            return;
        }

        var newX = Position.X;
        var newY = Position.Y;

        if (newX + windowWidth > screenRight)
        {
            newX = Math.Max(screenLeft, screenRight - windowWidth);
        }

        if (newX < screenLeft)
        {
            newX = screenLeft;
        }

        if (newY + windowHeight > screenBottom)
        {
            newY = Math.Max(screenTop, screenBottom - windowHeight);
        }

        if (newY < screenTop)
        {
            newY = screenTop;
        }

        if (newX != Position.X || newY != Position.Y)
        {
            Position = new PixelPoint(newX, newY);
        }
    }

    private void WireRequestClose()
    {
        if (DataContext is IRequestCloseViewModel viewModel)
        {
            _wiredViewModel = viewModel;
            viewModel.RequestClose += OnViewModelRequestClose;
        }
    }

    private void UnwireRequestClose()
    {
        if (_wiredViewModel != null)
        {
            _wiredViewModel.RequestClose -= OnViewModelRequestClose;
            _wiredViewModel = null;
        }
    }

    private void OnViewModelRequestClose(object? sender, EventArgs e)
    {
        Close();
    }
}
