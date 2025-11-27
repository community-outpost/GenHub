using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GenHub.Features.GitHub.Views;

/// <summary>
/// Interaction logic for GitHubTokenDialogView.xaml.
/// </summary>
public partial class GitHubTokenDialogView : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubTokenDialogView"/> class.
    /// </summary>
    public GitHubTokenDialogView()
    {
        InitializeComponent();
    }

    private void OnTokenPasswordChanged(object? sender, RoutedEventArgs e)
    {
        // NOTE: Avalonia's TextBox does not support SecureString binding directly.
        // The token briefly exists as plain text in memory during conversion.
        // This is a limitation of the current UI framework implementation.
        // Consider using a dedicated password control if available in future versions.
        if (sender is TextBox textBox && DataContext is ViewModels.GitHubTokenDialogViewModel viewModel)
        {
            // Convert string to SecureString for security
            var secureString = new System.Security.SecureString();
            foreach (char c in textBox.Text ?? string.Empty)
            {
                secureString.AppendChar(c);
            }

            viewModel.SetSecureToken(secureString);
        }
    }
}
