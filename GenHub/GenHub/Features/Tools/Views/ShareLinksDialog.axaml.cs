using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Features.Tools.ViewModels;
using System;

namespace GenHub.Features.Tools.Views;

/// <summary>
/// Dialog for sharing an upload via plain download link or GenHub protocol link.
/// </summary>
public partial class ShareLinksDialog : GenHubWindow
{
    private ShareLinksViewModel? attachedViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareLinksDialog"/> class.
    /// </summary>
    public ShareLinksDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (attachedViewModel is { } previous)
        {
            previous.CloseRequested -= OnCloseRequested;
        }

        if (DataContext is ShareLinksViewModel viewModel)
        {
            attachedViewModel = viewModel;
            viewModel.CloseRequested += OnCloseRequested;
        }
        else
        {
            attachedViewModel = null;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
}
