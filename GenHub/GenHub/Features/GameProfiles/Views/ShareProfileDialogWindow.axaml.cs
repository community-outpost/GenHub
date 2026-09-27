using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Features.GameProfiles.ViewModels;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Window for sharing game profiles via genhub:// URI, Discord invite, or .ghprofile export.
/// </summary>
public partial class ShareProfileDialogWindow : GenHubWindow
{
    private ShareProfileDialogViewModel? attachedViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShareProfileDialogWindow"/> class.
    /// </summary>
    public ShareProfileDialogWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <inheritdoc/>
    protected override bool DisposeDataContextOnClose => true;

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

        if (DataContext is ShareProfileDialogViewModel viewModel)
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
