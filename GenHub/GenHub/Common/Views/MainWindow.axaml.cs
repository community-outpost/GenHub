using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GenHub.Common.Controls;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Common.Views;

/// <summary>
/// Main application window for GenHub.
/// </summary>
public partial class MainWindow : GenHubWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    /// <inheritdoc/>
    protected override bool CloseOnEscape => false;

    /// <inheritdoc/>
    protected override bool FitToScreenOnOpen => false;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel { SelectedTab: NavigationTab.GameProfiles } && e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Suppress unhandled drag/drop exceptions to protect the UI event loop")]
    [SuppressMessage("Reliability", "CS-R1008", Justification = "Suppress unhandled drag/drop exceptions to protect the UI event loop")]
    private async void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (e.Handled || DataContext is not MainViewModel { SelectedTab: NavigationTab.GameProfiles } mainVm || mainVm.GameProfilesViewModel == null)
            {
                return;
            }

            var files = e.Data.GetFiles();
            if (files != null)
            {
                foreach (var file in files)
                {
                    if (file?.Path?.LocalPath is { } path &&
                        path.EndsWith(ProfileSharingConstants.ProfileFileExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        e.Handled = true;
                        await mainVm.GameProfilesViewModel.ImportProfileFromFileOrUriAsync(path);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to handle profile file drag-and-drop: {ex}");

            // Suppress unhandled drag/drop exceptions to protect the UI event loop
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
