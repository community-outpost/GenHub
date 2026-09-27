using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Features.GameProfiles.ViewModels;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Window for inspecting and importing shared game profiles.
/// </summary>
public partial class ImportProfileInspectionWindow : GenHubWindow
{
    private ImportProfileInspectionViewModel? attachedViewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportProfileInspectionWindow"/> class.
    /// </summary>
    public ImportProfileInspectionWindow()
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

        if (DataContext is ImportProfileInspectionViewModel viewModel)
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
