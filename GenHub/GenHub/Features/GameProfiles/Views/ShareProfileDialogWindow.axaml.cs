using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Window for sharing game profiles via genhub:// URI, Discord invite, or .ghprofile export.
/// </summary>
public partial class ShareProfileDialogWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShareProfileDialogWindow"/> class.
    /// </summary>
    public ShareProfileDialogWindow()
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
        if (DataContext is ShareProfileDialogViewModel viewModel)
        {
            viewModel.CloseRequested += (_, _) => Close();
        }
    }
}
