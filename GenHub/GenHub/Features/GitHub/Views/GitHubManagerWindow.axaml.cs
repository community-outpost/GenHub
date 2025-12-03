using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GenHub.Features.GitHub.Views;

/// <summary>
/// Main window for the GitHub manager feature.
/// </summary>
public partial class GitHubManagerWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubManagerWindow"/> class.
    /// </summary>
    public GitHubManagerWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
