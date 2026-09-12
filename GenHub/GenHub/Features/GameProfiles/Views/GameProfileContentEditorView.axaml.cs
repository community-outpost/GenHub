using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for editing game profile content.
/// </summary>
public partial class GameProfileContentEditorView : UserControl
{
    private static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(350);

    private readonly List<(string Name, Control Control, ContentEditorCategory Category)> _sections = [];

    private ScrollViewer? _scrollViewer;
    private GameProfileSettingsViewModel? _subscribedViewModel;
    private bool _isScrollingProgrammatically;

    // Animation state
    private DispatcherTimer? _animationTimer;
    private double _animStartOffset;
    private double _animTargetOffset;
    private DateTime _animStartTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileContentEditorView"/> class.
    /// </summary>
    public GameProfileContentEditorView()
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

        _scrollViewer = this.FindControl<ScrollViewer>("ContentEditorScrollViewer");
        if (_scrollViewer == null)
        {
            return;
        }

        // Map sections in top-to-bottom order (order matters for scroll spy)
        _sections.Clear();
        MapSection("EnabledContentSection", ContentEditorCategory.EnabledContent);
        MapSection("AvailableContentSection", ContentEditorCategory.AvailableContent);

        // Subscribe to DataContext changes to handle late binding
        DataContextChanged += OnDataContextChanged;

        // Try to set up now if DataContext is already available
        SetupScrollSpy();
    }

    /// <summary>
    /// Handles the unloaded event to clean up subscriptions.
    /// </summary>
    /// <param name="e">The event args.</param>
    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        StopAnimation();

        DataContextChanged -= OnDataContextChanged;

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.PointerWheelChanged -= OnPointerWheelChanged;
        }

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ScrollToSectionRequested -= OnScrollToSectionRequested;
            _subscribedViewModel = null;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        SetupScrollSpy();
    }

    private void SetupScrollSpy()
    {
        if (_scrollViewer == null || DataContext is not GameProfileSettingsViewModel vm)
        {
            return;
        }

        // Unsubscribe first to avoid duplicate subscriptions
        _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer.PointerWheelChanged -= OnPointerWheelChanged;
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ScrollToSectionRequested -= OnScrollToSectionRequested;
        }

        // Subscribe to scroll and input changes
        _scrollViewer.ScrollChanged += OnScrollChanged;
        _scrollViewer.PointerWheelChanged += OnPointerWheelChanged;

        _subscribedViewModel = vm;
        _subscribedViewModel.ScrollToSectionRequested += OnScrollToSectionRequested;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void MapSection(string name, ContentEditorCategory category)
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
        if (_isScrollingProgrammatically || _scrollViewer == null || DataContext is not GameProfileSettingsViewModel vm)
        {
            return;
        }

        var maxScrollY = _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height;
        var isAtBottom = maxScrollY > 0 && _scrollViewer.Offset.Y >= (maxScrollY - 25);

        if (isAtBottom && _sections.Count > 0)
        {
            var lastCategory = _sections[^1].Category;
            if (vm.SelectedContentEditorCategory != lastCategory)
            {
                vm.UpdateContentEditorCategoryFromScroll(lastCategory);
            }

            return;
        }

        var threshold = Math.Max(60, _scrollViewer.Viewport.Height * 0.35);
        ContentEditorCategory? activeCategory = null;

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

        if (activeCategory.HasValue && activeCategory.Value != vm.SelectedContentEditorCategory)
        {
            vm.UpdateContentEditorCategoryFromScroll(activeCategory.Value);
        }
        else if (!activeCategory.HasValue && _sections.Count > 0 && vm.SelectedContentEditorCategory != _sections[0].Category)
        {
            vm.UpdateContentEditorCategoryFromScroll(_sections[0].Category);
        }
    }
}