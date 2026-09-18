using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GenHub.Features.GitHub.ViewModels;
using System;

namespace GenHub.Features.GitHub.Views;

/// <summary>
/// Code-behind for the GitHub token dialog view.
/// </summary>
public partial class GitHubTokenDialogView : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubTokenDialogView"/> class.
    /// </summary>
    public GitHubTokenDialogView()
    {
        InitializeComponent();
        GenHub.Common.Helpers.WindowChromeHelper.ApplyPlatformDecorations(this);
    }

    /// <summary>
    /// Sets the view model and wires up events.
    /// </summary>
    /// <param name="viewModel">The view model to bind to.</param>
    public void SetViewModel(GitHubTokenDialogViewModel viewModel)
    {
        DataContext = viewModel;

        viewModel.SaveCompleted += () => Close(true);
        viewModel.CancelRequested += () => Close(false);
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is GitHubTokenDialogViewModel { IsValidating: true })
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
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
        if (DataContext is GitHubTokenDialogViewModel { IsValidating: true })
        {
            return;
        }

        Close(false);
    }

    private void OnTokenPasswordChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && DataContext is GitHubTokenDialogViewModel vm)
        {
            vm.SetToken(textBox.Text ?? string.Empty);
        }
    }
}
