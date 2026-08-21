using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for game settings (Options.ini) management with sidebar navigation and scroll spy.
/// </summary>
public partial class GameSettingsView : UserControl
{
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(350);

    private readonly List<(string Name, Control Control, SettingsCategory Category)> _sections = [];

    private ScrollViewer? _scrollViewer;
    private bool _isScrollingProgrammatically;

    // Animation state
    private DispatcherTimer? _animationTimer;
    private double _animStartOffset;
    private double _animTargetOffset;
    private DateTime _animStartTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameSettingsView"/> class.
    /// </summary>
    public GameSettingsView()
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

        _scrollViewer = this.FindControl<ScrollViewer>("SettingsScrollViewer");
        if (_scrollViewer == null)
        {
            return;
        }

        // Map sections in top-to-bottom order (order matters for scroll spy)
        _sections.Clear();
        MapSection("VideoSection", SettingsCategory.Video);
        MapSection("AudioSection", SettingsCategory.Audio);
        MapSection("ControlsSection", SettingsCategory.Controls);
        MapSection("TheSuperHackersSection", SettingsCategory.TheSuperHackers);
        MapSection("GeneralsOnlineSection", SettingsCategory.GeneralsOnline);

        if (DataContext is GameSettingsViewModel vm)
        {
            vm.ScrollToSectionRequested = OnScrollToSectionRequested;
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _scrollViewer.PointerWheelChanged -= OnPointerWheelChanged;
            _scrollViewer.PointerWheelChanged += OnPointerWheelChanged;
        }
    }

    /// <summary>
    /// Handles the unloaded event to clean up subscriptions.
    /// </summary>
    /// <param name="e">The event args.</param>
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        StopAnimation();

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.PointerWheelChanged -= OnPointerWheelChanged;
        }

        if (DataContext is GameSettingsViewModel vm)
        {
            vm.ScrollToSectionRequested = null;
        }
    }

    private void MapSection(string name, SettingsCategory category)
    {
        var control = this.FindControl<Control>(name);
        if (control != null)
        {
            _sections.Add((name, control, category));
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_isScrollingProgrammatically)
        {
            StopAnimation();
        }
    }

    private void OnScrollToSectionRequested(string sectionName)
    {
        if (_scrollViewer == null)
        {
            return;
        }

        Control? targetControl = null;
        foreach (var section in _sections)
        {
            if (section.Name == sectionName)
            {
                targetControl = section.Control;
                break;
            }
        }

        if (targetControl == null || _scrollViewer.Content is not Control content)
        {
            return;
        }

        var transform = targetControl.TransformToVisual(content);
        if (!transform.HasValue)
        {
            return;
        }

        var pos = transform.Value.Transform(new Point(0, 0));
        var maxScrollY = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        var targetY = Math.Clamp(pos.Y, 0, maxScrollY);

        StartAnimation(targetY);
    }

    private void StartAnimation(double targetY)
    {
        if (_scrollViewer == null)
        {
            return;
        }

        StopAnimationTimer();

        var currentY = _scrollViewer.Offset.Y;
        if (Math.Abs(currentY - targetY) < 1.0)
        {
            _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, targetY);
            _isScrollingProgrammatically = false;
            return;
        }

        _isScrollingProgrammatically = true;
        _animStartOffset = currentY;
        _animTargetOffset = targetY;
        _animStartTime = DateTime.UtcNow;

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void StopAnimationTimer()
    {
        if (_animationTimer != null)
        {
            _animationTimer.Tick -= OnAnimationTick;
            _animationTimer.Stop();
            _animationTimer = null;
        }
    }

    private void StopAnimation()
    {
        StopAnimationTimer();
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
            StopAnimationTimer();
            Dispatcher.UIThread.Post(() => _isScrollingProgrammatically = false, DispatcherPriority.Normal);
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isScrollingProgrammatically || _scrollViewer == null || DataContext is not GameSettingsViewModel vm)
        {
            return;
        }

        var maxScrollY = _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height;
        var isAtBottom = maxScrollY > 0 && _scrollViewer.Offset.Y >= (maxScrollY - 25);

        if (isAtBottom && _sections.Count > 0)
        {
            var lastCategory = _sections[^1].Category;
            if (vm.SelectedCategory != lastCategory)
            {
                vm.UpdateCategoryFromScroll(lastCategory);
            }

            return;
        }

        var threshold = Math.Max(60, _scrollViewer.Viewport.Height * 0.35);
        SettingsCategory? activeCategory = null;

        foreach (var (_, control, category) in _sections)
        {
            try
            {
                var transform = control.TransformToVisual(_scrollViewer);
                if (!transform.HasValue)
                {
                    continue;
                }

                var position = transform.Value.Transform(new Point(0, 0));

                if (position.Y <= threshold)
                {
                    activeCategory = category;
                }
            }
            catch
            {
                // Visual tree detachment safety
            }
        }

        if (activeCategory.HasValue && activeCategory.Value != vm.SelectedCategory)
        {
            vm.UpdateCategoryFromScroll(activeCategory.Value);
        }
        else if (!activeCategory.HasValue && _sections.Count > 0 && vm.SelectedCategory != _sections[0].Category)
        {
            vm.UpdateCategoryFromScroll(_sections[0].Category);
        }
    }
}
