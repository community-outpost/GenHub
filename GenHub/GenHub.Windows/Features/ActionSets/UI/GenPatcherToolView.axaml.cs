using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Windows.Features.ActionSets.UI;

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

    private async void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Only initialize once
        AttachedToVisualTree -= OnAttachedToVisualTree;

        if (DataContext is GenPatcherViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
