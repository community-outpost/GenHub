using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Core.Constants;
using GenHub.Features.GameProfiles.ViewModels;
using System;
using System.IO;
using System.Linq;

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
        if (DataContext is not GameProfileLauncherViewModel vm)
        {
            return;
        }

        var files = e.Data.GetFiles();
        if (files == null)
        {
            return;
        }

        var paths = files
            .Select(f => f.Path?.LocalPath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToList();

        if (paths.Count == 0)
        {
            return;
        }

        var profilePath = paths.FirstOrDefault(p =>
            p.EndsWith(ProfileSharingConstants.ProfileFileExtension, StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(FileTypes.JsonFileExtension, StringComparison.OrdinalIgnoreCase));

        e.Handled = true;
        if (profilePath != null)
        {
            await vm.ImportProfileFromFileOrUriAsync(profilePath);
        }
        else
        {
            await vm.HandleDroppedContentAsync(paths);
        }
    }
}
