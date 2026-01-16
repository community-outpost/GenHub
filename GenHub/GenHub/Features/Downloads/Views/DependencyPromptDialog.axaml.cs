using Avalonia.Controls;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// Dialog window for prompting users about missing dependencies.
/// </summary>
public partial class DependencyPromptDialog : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyPromptDialog"/> class.
    /// </summary>
    public DependencyPromptDialog()
    {
        InitializeComponent();
    }
}
