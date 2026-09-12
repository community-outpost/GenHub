using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Features.GitHub.ViewModels;

namespace GenHub.Features.GitHub.Views;

/// <summary>
/// code-behind for the github token dialog view.
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
    /// sets the view model and wires up events.
    /// </summary>
    /// <param name="viewModel">the view model to bind to.</param>
    public void SetViewModel(GitHubTokenDialogViewModel viewModel)
    {
        DataContext = viewModel;

        viewModel.SaveCompleted += () => Close(true);
        viewModel.CancelRequested += () => Close(false);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
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