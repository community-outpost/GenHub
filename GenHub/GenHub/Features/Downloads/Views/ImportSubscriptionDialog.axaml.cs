using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GenHub.Features.Downloads.ViewModels;
using System;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Code-behind for the Import Subscription dialog.
/// </summary>
public partial class ImportSubscriptionDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImportSubscriptionDialog"/> class.
    /// </summary>
    public ImportSubscriptionDialog()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ImportSubscriptionViewModel vm)
        {
            vm.RequestClose = _ => Close();
        }
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
