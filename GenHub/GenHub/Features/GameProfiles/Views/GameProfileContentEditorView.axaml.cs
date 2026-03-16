using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16); // ~60fps

    private readonly List<(string Name, Control Control, ContentEditorCategory Category)> _sections = [];

    private ScrollViewer? _scrollViewer;
    private GameProfileSettingsViewModel? _subscribedViewModel;
    private bool _isScrollingProgrammatically;

    // Animation state
    private DispatcherTimer? _animationTimer;
    private Stopwatch _animationStopwatch = new();
    private double _animStartOffset;
    private double _animTargetOffset;

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
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.ScrollToSectionRequested -= OnScrollToSectionRequested;
        }

        // Subscribe to scroll changes
        _scrollViewer.ScrollChanged += OnScrollChanged;

        // Add our handler to the multicast delegate (don't replace other views' handlers)
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

    private void OnScrollToSectionRequested(string sectionName)
    {
        if (_scrollViewer == null)
        {
            return;
        }

        // Find the target section
        Control? targetControl = null;
        foreach (var section in _sections)
        {
            if (section.Name == sectionName)
            {
                targetControl = section.Control;
                break;
            }
        }

        if (targetControl == null)
        {
            return;
        }

        // Calculate target offset
        if (_scrollViewer.Content is not Control content)
        {
            return;
        }

        var transform = targetControl.TransformToVisual(content);
        if (!transform.HasValue)
        {
            return;
        }

        var pos = transform.Value.Transform(new Point(0, 0));
        var targetY = Math.Max(0, Math.Min(pos.Y, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height));

        // Start smooth scroll animation
        StartAnimation(_scrollViewer.Offset.Y, targetY);
    }

    private void StartAnimation(double fromY, double toY)
    {
        StopAnimation();

        _isScrollingProgrammatically = true;
        _animStartOffset = fromY;
        _animTargetOffset = toY;
        _animationStopwatch.Restart();

        _animationTimer = new DispatcherTimer { Interval = FrameInterval };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void StopAnimation()
    {
        if (_animationTimer != null)
        {
            _animationTimer.Tick -= OnAnimationTick;
            _animationTimer.Stop();
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

        var elapsed = _animationStopwatch.Elapsed;
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

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isScrollingProgrammatically || _scrollViewer == null || DataContext is not GameProfileSettingsViewModel vm)
        {
            return;
        }

        // Find the last section whose top is at or above the viewport top
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

                // Section is at or above viewport top (with small buffer)
                if (position.Y <= 50)
                {
                    activeCategory = category;
                }
            }
            catch
            {
                // Ignore visual tree detachment errors
            }
        }

        if (activeCategory.HasValue && activeCategory.Value != vm.SelectedContentEditorCategory)
        {
            vm.UpdateContentEditorCategoryFromScroll(activeCategory.Value);
        }
        else if (!activeCategory.HasValue && vm.SelectedContentEditorCategory != ContentEditorCategory.EnabledContent)
        {
            vm.UpdateContentEditorCategoryFromScroll(ContentEditorCategory.EnabledContent);
        }
    }
}