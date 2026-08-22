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
/// View for general profile settings (Identity, Theme, etc.).
/// </summary>
public partial class GameProfileGeneralSettingsView : UserControl
{
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(350);
    private readonly List<(string Name, Control Control, GeneralSettingsCategory Category)> _sections = [];
    private ScrollViewer? _scrollViewer;
    private bool _isScrollingProgrammatically;
    private DispatcherTimer? _animationTimer;
    private double _animTargetOffset;
    private double _animStartOffset;
    private DateTime _animStartTime;
    private GameProfileSettingsViewModel? _boundViewModel;

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
        MapSections();

        if (DataContext is GameProfileSettingsViewModel vm)
        {
            _boundViewModel = vm;
            AttachHandlers(vm);
        }
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_boundViewModel != null)
        {
            DetachHandlers(_boundViewModel);
            _boundViewModel = null;
        }

        if (DataContext is GameProfileSettingsViewModel vm)
        {
            _boundViewModel = vm;
            AttachHandlers(vm);
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

        if (_boundViewModel != null)
        {
            DetachHandlers(_boundViewModel);
            _boundViewModel = null;
        }
    }

    private void MapSections()
    {
        if (_sections.Count > 0)
        {
            return;
        }

        MapSection("IdentitySection", GeneralSettingsCategory.Identity);
        MapSection("AppearanceSection", GeneralSettingsCategory.Appearance);
        MapSection("LaunchSection", GeneralSettingsCategory.Launch);
        MapSection("ThemeSection", GeneralSettingsCategory.Theme);
    }

    private void AttachHandlers(GameProfileSettingsViewModel vm)
    {
        vm.ScrollToSectionRequested -= OnScrollToSectionRequested;
        vm.ScrollToSectionRequested += OnScrollToSectionRequested;

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _scrollViewer.PointerWheelChanged -= OnPointerWheelChanged;
            _scrollViewer.PointerWheelChanged += OnPointerWheelChanged;
        }
    }

    private void DetachHandlers(GameProfileSettingsViewModel vm)
    {
        vm.ScrollToSectionRequested -= OnScrollToSectionRequested;
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.PointerWheelChanged -= OnPointerWheelChanged;
        }
    }

    private void MapSection(string name, GeneralSettingsCategory category)
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
        foreach (var (name, control, _) in _sections)
        {
            if (name == sectionName)
            {
                targetControl = control;
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

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isScrollingProgrammatically || _scrollViewer == null || DataContext is not GameProfileSettingsViewModel vm)
        {
            return;
        }

        var maxScrollY = _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height;
        var isAtBottom = maxScrollY > 0 && _scrollViewer.Offset.Y >= (maxScrollY - 25);

        if (isAtBottom && _sections.Count > 0)
        {
            var lastCategory = _sections[^1].Category;
            if (vm.SelectedGeneralCategory != lastCategory)
            {
                vm.UpdateGeneralCategoryFromScroll(lastCategory);
            }

            return;
        }

        var threshold = Math.Max(60, _scrollViewer.Viewport.Height * 0.35);
        GeneralSettingsCategory? activeCategory = null;

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
            catch (InvalidOperationException)
            {
                // Ignore transformation errors
            }
        }

        if (activeCategory.HasValue && activeCategory.Value != vm.SelectedGeneralCategory)
        {
            vm.UpdateGeneralCategoryFromScroll(activeCategory.Value);
        }
        else if (!activeCategory.HasValue && _sections.Count > 0 && vm.SelectedGeneralCategory != _sections[0].Category)
        {
            vm.UpdateGeneralCategoryFromScroll(_sections[0].Category);
        }
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

        _animationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
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
}
