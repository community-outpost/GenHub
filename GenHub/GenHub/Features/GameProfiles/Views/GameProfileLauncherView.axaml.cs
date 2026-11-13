using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Core.Constants;
using GenHub.Features.GameProfiles.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.Json;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for the Game Profiles feature.
/// </summary>
public partial class GameProfileLauncherView : UserControl
{
    // Height of the invisible hover/tap strip at the top of the view that re-expands the
    // collapsed header. Tracked via pointer position so it reserves no layout space.
    private const double HeaderHoverThreshold = 24.0;

    private bool _pointerInHeaderHoverZone;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileLauncherView"/> class.
    /// </summary>
    public GameProfileLauncherView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    /// <summary>
    /// Determines whether a JSON file has the structure of a shared profile package.
    /// </summary>
    /// <param name="path">The local file path to inspect.</param>
    /// <returns>True when the file contains a profile package object; otherwise false.</returns>
    private static bool IsProfilePackageJson(string path)
    {
        try
        {
            // Bound the sniff cost: oversized files cannot be profile packages and fall through to content import.
            if (new FileInfo(path).Length > ProfileSharingConstants.MaxProfileFileBytes)
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "profile", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Object)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or SecurityException or ArgumentException)
        {
            return false;
        }
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

    private void RootLayout_PointerMoved(object? sender, PointerEventArgs e)
    {
        UpdateHeaderHoverZone(e);
    }

    private void RootLayout_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        UpdateHeaderHoverZone(e);
    }

    private void UpdateHeaderHoverZone(PointerEventArgs e)
    {
        if (DataContext is not GameProfileLauncherViewModel vm || vm.IsHeaderExpanded)
        {
            _pointerInHeaderHoverZone = false;
            return;
        }

        var inHoverZone = e.GetPosition(this).Y <= HeaderHoverThreshold;
        if (inHoverZone == _pointerInHeaderHoverZone)
        {
            return;
        }

        _pointerInHeaderHoverZone = inHoverZone;
        if (inHoverZone)
        {
            vm.ExpandHeaderCommand.Execute(null);
        }
        else
        {
            vm.StartHeaderTimerCommand.Execute(null);
        }
    }

    private void SortModeComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (DataContext is GameProfileLauncherViewModel vm)
        {
            vm.IsSortDropdownOpen = true;
            vm.ExpandHeaderCommand.Execute(null);
        }
    }

    private void SortModeComboBox_DropDownClosed(object? sender, EventArgs e)
    {
        if (DataContext is GameProfileLauncherViewModel vm)
        {
            vm.IsSortDropdownOpen = false;
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
            (p.EndsWith(FileTypes.JsonFileExtension, StringComparison.OrdinalIgnoreCase) && IsProfilePackageJson(p)));

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
