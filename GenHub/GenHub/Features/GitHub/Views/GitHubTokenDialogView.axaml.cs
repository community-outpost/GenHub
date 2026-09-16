using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Features.GitHub.ViewModels;

namespace GenHub.Features.GitHub.Views;

/// <summary>
/// Code-behind for the GitHub Token Dialog view.
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

    /// <summary>
    /// Sets the ViewModel and wires up events.
    /// </summary>
    /// <param name="viewModel">The ViewModel to bind to.</param>
    public void SetViewModel(GitHubTokenDialogViewModel viewModel)
    {
        DataContext = viewModel;

        viewModel.SaveCompleted += () => Close(true);
        viewModel.CancelRequested += () => Close(false);
    }

    private void OnTokenPasswordChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && DataContext is GitHubTokenDialogViewModel vm)
        {
            vm.SetToken(textBox.Text ?? string.Empty);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
