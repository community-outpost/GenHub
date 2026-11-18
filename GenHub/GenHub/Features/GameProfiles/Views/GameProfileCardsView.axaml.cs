using Avalonia.Controls;
using Avalonia.Input;
using GenHub.Features.GameProfiles.ViewModels;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Card gallery layout for game profiles.
/// </summary>
public partial class GameProfileCardsView : UserControl
{
    // Height of the invisible hover/tap strip at the top of the view that re-expands the
    // collapsed header. Tracked via pointer position so it reserves no layout space.
    private const double HeaderHoverThreshold = 24.0;

    private bool _pointerInHeaderHoverZone;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileCardsView"/> class.
    /// </summary>
    public GameProfileCardsView()
    {
        InitializeComponent();
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
}
