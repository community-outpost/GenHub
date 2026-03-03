using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for general profile settings (Identity, Theme, etc.).
/// </summary>
public partial class GameProfileGeneralSettingsView : UserControl
{
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(350);
    private readonly Dictionary<string, Control> _sections = [];
    private readonly Dictionary<string, GeneralSettingsCategory> _sectionToCategoryMap = [];
    private ScrollViewer? _scrollViewer;
    private bool _isScrollingProgrammatically;
    private DispatcherTimer? _animationTimer;
    private double _animTargetOffset;
    private double _animStartOffset;
    private DateTime _animStartTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileGeneralSettingsView"/> class.
    /// </summary>
    public GameProfileGeneralSettingsView()
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

        _scrollViewer = this.FindControl<ScrollViewer>("GeneralSettingsScrollViewer");
        if (_scrollViewer == null)
        {
            return;
        }

        // Map section names to controls and categories
        MapSection("IdentitySection", GeneralSettingsCategory.Identity);
        MapSection("AppearanceSection", GeneralSettingsCategory.Appearance);
        MapSection("LaunchSection", GeneralSettingsCategory.Launch);
        MapSection("ThemeSection", GeneralSettingsCategory.Theme);

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

    private void MapSection(string name, GeneralSettingsCategory category)
    {
        var control = this.FindControl<Control>(name);
        if (control != null)
        {
            _sections[name] = control;
            _sectionToCategoryMap[name] = category;
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
                var content = _scrollViewer.Content as Control;
                if (content != null)
                {
                    var transform = targetControl.TransformToVisual(content);
                    if (transform.HasValue)
                    {
                        var pos = transform.Value.Transform(new Point(0, 0));
                        StartAnimation(pos.Y);
                    }
                }

                _isScrollingProgrammatically = false;
            },
            DispatcherPriority.Background);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isScrollingProgrammatically || _scrollViewer == null || DataContext is not GameProfileSettingsViewModel vm)
        {
            return;
        }

        GeneralSettingsCategory? activeCategory = null;

        foreach (var kvp in _sections)
        {
            var section = kvp.Value;
            var category = _sectionToCategoryMap[kvp.Key];

            try
            {
                var transform = section.TransformToVisual(_scrollViewer);
                if (transform == null)
                {
                    continue;
                }

                var position = transform.Value.Transform(new Point(0, 0));

                if (position.Y <= 50)
                {
                    activeCategory = category;
                }
            }
            catch
            {
                // Ignore transformation errors
            }
        }

        if (activeCategory.HasValue && activeCategory.Value != vm.SelectedGeneralCategory)
        {
            vm.UpdateGeneralCategoryFromScroll(activeCategory.Value);
        }
    }

    private void StartAnimation(double targetY)
    {
        if (_scrollViewer == null) return;

        StopAnimation();

        _animStartOffset = _scrollViewer.Offset.Y;
        _animTargetOffset = targetY;
        _animStartTime = DateTime.UtcNow;

        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void StopAnimation()
    {
        if (_animationTimer != null)
        {
            _animationTimer.Stop();
            _animationTimer.Tick -= OnAnimationTick;
            _animationTimer = null;
        }

        _isScrollingProgrammatically = false;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_scrollViewer == null)
        {
            StopAnimation();
            return;
        }

        var elapsed = DateTime.UtcNow - _animStartTime;
        var t = Math.Min(1.0, elapsed.TotalMilliseconds / AnimationDuration.TotalMilliseconds);

        // Ease-in-out quadratic
        var eased = t < 0.5
            ? 2.0 * (t * t)
            : 1.0 - (Math.Pow((-2.0 * t) + 2.0, 2) / 2.0);

        var currentY = _animStartOffset + ((_animTargetOffset - _animStartOffset) * eased);
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, currentY);

        if (t >= 1.0)
        {
            StopAnimation();
        }
    }
}
