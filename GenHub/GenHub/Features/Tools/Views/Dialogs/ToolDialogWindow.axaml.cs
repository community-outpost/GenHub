using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace GenHub.Features.Tools.Views.Dialogs;

/// <summary>
/// Generic window for hosting tool dialogs.
/// </summary>
public partial class ToolDialogWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ToolDialogWindow"/> class.
    /// </summary>
    public ToolDialogWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the dialog content inside the MainContent placeholder.
    /// </summary>
    /// <param name="content">The content control to display.</param>
    public void SetDialogContent(Control content)
    {
        var mainContent = this.FindControl<ContentControl>("MainContent");
        if (mainContent != null)
        {
            mainContent.Content = content;
        }
        else
        {
            Content = content;
        }
    }

    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToScreen();
    }

    /// <inheritdoc/>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        EnsurePositionWithinScreen();
    }

    /// <inheritdoc/>
    /// <param name="e">The key event arguments.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnDragRegionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

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

        MaxWidth = Math.Min(Math.Max(400, maxDipsWidth), availableWidth);
        MaxHeight = Math.Min(Math.Max(300, maxDipsHeight), availableHeight);

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
}
