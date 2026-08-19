using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Window for inspecting and importing shared game profiles.
/// </summary>
public partial class ImportProfileInspectionWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ImportProfileInspectionWindow"/> class.
    /// </summary>
    public ImportProfileInspectionWindow()
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
        if (DataContext is ImportProfileInspectionViewModel viewModel)
        {
            viewModel.CloseRequested += (_, _) => Close();
        }
    }
}
