namespace GenHub.Windows.Features.ActionSets.UI;

using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

/// <summary>
/// View for the GenPatcher tool.
/// </summary>
public partial class GenPatcherToolView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GenPatcherToolView"/> class.
    /// </summary>
    public GenPatcherToolView()
    {
        InitializeComponent();

        // Trigger initialization when the view is actually loaded
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private async void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // Only initialize once
        AttachedToVisualTree -= OnAttachedToVisualTree;

        if (DataContext is GenPatcherViewModel vm)
        {
            try
            {
                await vm.InitializeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GenPatcherToolView] Initialization error: {ex.Message}");
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
