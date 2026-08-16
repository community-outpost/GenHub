using Avalonia.Controls;
using GenHub.Features.Tools.ModBuilder.ViewModels;

namespace GenHub.Features.Tools.ModBuilder.Views;

/// <summary>
/// Dialog for editing ModBuilder configuration (bundle items and packs).
/// </summary>
public partial class ConfigEditorDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigEditorDialog"/> class.
    /// </summary>
    public ConfigEditorDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigEditorDialog"/> class with a ViewModel.
    /// </summary>
    /// <param name="viewModel">The ViewModel for this dialog.</param>
    public ConfigEditorDialog(ConfigEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}
