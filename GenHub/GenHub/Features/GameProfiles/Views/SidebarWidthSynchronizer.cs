using Avalonia;
using Avalonia.Controls;
using GenHub.Core.Constants;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Manages and persists sidebar grid column widths per profile settings tab view.
/// </summary>
public sealed class SidebarWidthSynchronizer : IDisposable
{
    private readonly ProfileSettingsTab _tab;
    private ColumnDefinition? _sidebarColumn;
    private bool _isDisposed;

    private SidebarWidthSynchronizer(Grid grid, ProfileSettingsTab tab)
    {
        _tab = tab;

        if (grid.ColumnDefinitions.Count > 0)
        {
            _sidebarColumn = grid.ColumnDefinitions[0];
            var savedWidth = GameProfileSettingsWindow.GetTabSidebarWidth(tab);

            if (savedWidth.HasValue && savedWidth.Value > 0)
            {
                _sidebarColumn.Width = new GridLength(savedWidth.Value, GridUnitType.Pixel);
            }
            else
            {
                _sidebarColumn.Width = new GridLength(UiConstants.DefaultProfileSettingsTabSidebarFallbackWidth, GridUnitType.Pixel);
            }

            _sidebarColumn.PropertyChanged += OnColumnPropertyChanged;
        }
    }

    /// <summary>
    /// Attaches sidebar width management to the specified grid's first column for a given tab.
    /// </summary>
    /// <param name="grid">The grid containing the sidebar in column 0.</param>
    /// <param name="tab">The profile settings tab identity.</param>
    /// <returns>A synchronizer instance to dispose when unloaded, or null if the grid is null.</returns>
    public static SidebarWidthSynchronizer? Attach(Grid? grid, ProfileSettingsTab tab)
    {
        return grid != null ? new SidebarWidthSynchronizer(grid, tab) : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_sidebarColumn != null)
        {
            _sidebarColumn.PropertyChanged -= OnColumnPropertyChanged;
            _sidebarColumn = null;
        }
    }

    private void OnColumnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ColumnDefinition.WidthProperty && e.NewValue is GridLength w)
        {
            if (w.IsAbsolute && w.Value > 0)
            {
                var currentSaved = GameProfileSettingsWindow.GetTabSidebarWidth(_tab);
                if (!currentSaved.HasValue || Math.Abs(w.Value - currentSaved.Value) > 0.5)
                {
                    GameProfileSettingsWindow.UpdateTabSidebarWidth(_tab, w.Value);
                }
            }
            else if (w.IsAuto)
            {
                GameProfileSettingsWindow.ResetTabSidebarWidth(_tab);
            }
        }
    }
}
