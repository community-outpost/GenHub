using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Features.GameProfiles.ViewModels.Wizard;
using System;

namespace GenHub.Features.GameProfiles.Views.Wizard;

/// <summary>
/// Interaction logic for the Setup Wizard dialog.
/// </summary>
public partial class SetupWizardView : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SetupWizardView"/> class.
    /// </summary>
    public SetupWizardView()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    /// <summary>
    /// Handles the DataContextChanged event to wire up view model events.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SetupWizardViewModel vm)
        {
            vm.CloseRequested -= OnCloseRequested;
            vm.CloseRequested += OnCloseRequested;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        Close();
    }
}
