using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Features.Tools.ModBuilder.ViewModels;

namespace GenHub.Features.Tools.ModBuilder.Views;

/// <summary>
/// Dialog window for importing a GitHub repository as a ModBuilder project.
/// </summary>
public partial class GitHubImportDialog : Window
{
    /// <summary>
    /// Gets the validated repository reference when the dialog confirms.
    /// </summary>
    public GitHubRepositoryReference? ResultReference { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubImportDialog"/> class.
    /// </summary>
    public GitHubImportDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubImportDialog"/> class with a ViewModel.
    /// </summary>
    /// <param name="viewModel">The ViewModel.</param>
    public GitHubImportDialog(GitHubImportViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    /// <inheritdoc/>
    /// <param name="e">The key event arguments.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            e.Handled = true;
            Close(false);
        }

        if (e.Key == Key.Enter && !e.Handled)
        {
            e.Handled = true;
            Confirm();
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2 && CanResize)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void Confirm()
    {
        if (DataContext is GitHubImportViewModel vm)
        {
            var reference = vm.Validate();
            if (reference == null)
            {
                return;
            }

            ResultReference = reference;
            Close(true);
        }
        else
        {
            Close(false);
        }
    }
}
