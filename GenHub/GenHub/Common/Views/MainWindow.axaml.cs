using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using GenHub.Common.Helpers;
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
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        WindowChromeHelper.ApplyPlatformDecorations(this);

        var resizeGrips = this.FindControl<Panel>("LinuxResizeGrips");
        if (resizeGrips is not null)
        {
            WindowChromeHelper.AttachResizeGrips(this, resizeGrips);
        }

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

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

    /// <summary>
    /// Handles pointer pressed events on the title bar for dragging.
    /// </summary>
    /// <param name="sender">The sender object.</param>
    /// <param name="e">The pointer event arguments.</param>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2 && CanResize)
            {
                MaximizeButton_Click(sender, new Avalonia.Interactivity.RoutedEventArgs());
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    /// <summary>
    /// Handles the minimize button click.
    /// </summary>
    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Handles the maximize/restore button click.
    /// </summary>
    private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>
    /// Handles the close button click.
    /// </summary>
    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
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
