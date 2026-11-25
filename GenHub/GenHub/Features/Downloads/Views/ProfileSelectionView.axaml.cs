using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Features.Downloads.ViewModels;
using System;

namespace GenHub.Features.Downloads.Views;

/// <summary>
/// dialog window for selecting a profile to add content to.
/// displays compatible profiles first, followed by incompatible profiles with warnings.
/// </summary>
public partial class ProfileSelectionView : GenHubWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSelectionView"/> class.
    /// </summary>
    public ProfileSelectionView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSelectionView"/> class with a specific view model.
    /// </summary>
    /// <param name="viewModel">The profile selection view model.</param>
    public ProfileSelectionView(ProfileSelectionViewModel viewModel)
        : this()
    {
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    /// <inheritdoc />
    protected override bool DisposeDataContextOnClose => true;

    /// <inheritdoc/>
    protected override void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileSelectionViewModel viewModel)
        {
            viewModel.CancelCommand.Execute(null);
        }
        else
        {
            Close();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
