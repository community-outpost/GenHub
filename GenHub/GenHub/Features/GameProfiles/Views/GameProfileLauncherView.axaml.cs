using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Core.Constants;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for the Game Profiles feature.
/// </summary>
public partial class GameProfileLauncherView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileLauncherView"/> class.
    /// </summary>
    public GameProfileLauncherView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    private void HeaderZone_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (DataContext is GameProfileLauncherViewModel vm)
        {
            vm.ExpandHeaderCommand.Execute(null);
        }
    }

    private void HeaderZone_PointerExited(object? sender, PointerEventArgs e)
    {
        if (DataContext is GameProfileLauncherViewModel vm)
        {
            vm.StartHeaderTimerCommand.Execute(null);
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not GameProfileLauncherViewModel vm) return;

        var files = e.Data.GetFiles();
        if (files != null)
        {
            foreach (var file in files)
            {
                if (file?.Path?.LocalPath is { } path &&
                    (path.EndsWith(ProfileSharingConstants.ProfileFileExtension, StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                {
                    e.Handled = true;
                    await vm.ImportProfileFromFileOrUriAsync(path);
                    break;
                }
            }
        }
    }
}
