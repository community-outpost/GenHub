using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

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
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is GenHub.Features.Downloads.ViewModels.ImportSubscriptionViewModel vm)
        {
            vm.RequestClose = _ => Close();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
