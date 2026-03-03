using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for game content settings (Enabled content, Mod browser, etc.).
/// </summary>
public partial class GameProfileContentSettingsView : UserControl
{
    private readonly Dictionary<string, Control> _sections = [];
    private ScrollViewer? _scrollViewer;
    private bool _isScrollingProgrammatically;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileContentSettingsView"/> class.
    /// </summary>
    public GameProfileContentSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles the loaded event to bind the ViewModel command to the View's scroll logic.
    /// </summary>
    /// <param name="e">The event args.</param>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _scrollViewer = this.FindControl<ScrollViewer>("ContentSettingsScrollViewer");
        if (_scrollViewer == null)
        {
            return;
        }

        // Map section names to controls
        MapSection("SelectionSection");
        MapSection("DiscoverySection");

        if (DataContext is GameProfileSettingsViewModel vm)
        {
            // Subscribe to ViewModel scroll requests
            vm.ScrollToSectionRequested = OnScrollToSectionRequested;

            // Subscribe to ScrollViewer changes for Spy logic
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    /// <summary>
    /// Handles the unloaded event to clean up subscriptions.
    /// </summary>
    /// <param name="e">The event args.</param>
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
        }

        if (DataContext is GameProfileSettingsViewModel vm)
        {
            vm.ScrollToSectionRequested = null;
        }
    }

    private void MapSection(string name)
    {
        var control = this.FindControl<Control>(name);
        if (control != null)
        {
            _sections[name] = control;
        }
    }

    private void OnScrollToSectionRequested(string sectionName)
    {
        if (_scrollViewer == null || !_sections.TryGetValue(sectionName, out var targetControl))
        {
            return;
        }

        _isScrollingProgrammatically = true;

        Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (_scrollViewer.Content is Control content)
                {
                    var transform = targetControl.TransformToVisual(content);
                    if (transform.HasValue)
                    {
                        var pos = transform.Value.Transform(new Point(0, 0));
                        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, pos.Y);
                    }
                }

                // Re-enable scroll spy after a short delay
                Dispatcher.UIThread.InvokeAsync(() => _isScrollingProgrammatically = false, DispatcherPriority.Input);
            },
            DispatcherPriority.Background);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isScrollingProgrammatically || _scrollViewer == null)
        {
            return;
        }

        // Simple scroll spy logic can be implemented here if needed to update SelectedContentCategory
    }
}
