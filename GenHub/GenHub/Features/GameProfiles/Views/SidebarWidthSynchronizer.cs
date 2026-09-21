using Avalonia;
using Avalonia.Controls;
using System;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Synchronizes the sidebar grid column width across profile settings tab views.
/// </summary>
public sealed class SidebarWidthSynchronizer : IDisposable
{
    private readonly Grid _grid;
    private IDisposable? _columnWidthSubscription;
    private bool _isDisposed;

    private SidebarWidthSynchronizer(Grid grid)
    {
        _grid = grid;
        if (_grid.ColumnDefinitions.Count > 0)
        {
            var sidebarCol = _grid.ColumnDefinitions[0];
            sidebarCol.Width = new GridLength(GameProfileSettingsWindow.SavedSidebarWidth);
            _columnWidthSubscription = sidebarCol.GetObservable(ColumnDefinition.WidthProperty)
                .Subscribe(w =>
                {
                    if (w.IsAbsolute && w.Value > 0 && Math.Abs(w.Value - GameProfileSettingsWindow.SavedSidebarWidth) > 0.5)
                    {
                        GameProfileSettingsWindow.UpdateSidebarWidth(w.Value);
                    }
                });
            GameProfileSettingsWindow.SidebarWidthChanged += OnSidebarWidthChanged;
        }
    }

    /// <summary>
    /// Attaches width synchronization to the specified grid's first column.
    /// </summary>
    /// <param name="grid">The grid containing the sidebar in column 0.</param>
    /// <returns>A synchronizer instance to dispose when unloaded, or null if the grid is null.</returns>
    public static SidebarWidthSynchronizer? Attach(Grid? grid)
    {
        return grid != null ? new SidebarWidthSynchronizer(grid) : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        GameProfileSettingsWindow.SidebarWidthChanged -= OnSidebarWidthChanged;
        _columnWidthSubscription?.Dispose();
        _columnWidthSubscription = null;
    }

    private void OnSidebarWidthChanged(object? sender, double width)
    {
        if (_grid.ColumnDefinitions.Count > 0)
        {
            var col = _grid.ColumnDefinitions[0];
            if (col.Width.IsAbsolute && Math.Abs(col.Width.Value - width) > 0.5)
            {
                col.Width = new GridLength(width);
            }
        }
    }
}
