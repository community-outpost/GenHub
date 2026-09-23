using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using GenHub.Core.Constants;
using System;
using System.Collections.Generic;

namespace GenHub.Common.Controls;

/// <summary>
/// Coordinates sidebar-to-content animated scrolling and content-to-sidebar highlight tracking
/// for settings-style pages with a scrollable stack of sections.
/// </summary>
/// <typeparam name="TKey">The type of the section identifier.</typeparam>
public sealed class SectionScrollSpy<TKey>(ScrollViewer scrollViewer, Action<TKey> activeSectionChanged) : IDisposable
    where TKey : notnull
{
    private readonly TimeSpan _animationDuration = TimeSpan.FromMilliseconds(ScrollSpyConstants.AnimationDurationMs);
    private readonly List<(TKey Key, Control Control)> _sections = [];
    private DispatcherTimer? _animationTimer;
    private Control? _animTargetControl;
    private double _animStartOffset;
    private double _animTargetOffset;
    private DateTime _animStartTime;
    private int _animationGeneration;
    private (bool HasValue, TKey Value) _animTargetKey;
    private TKey _lastReportedKey = default!;
    private bool _hasReportedKey;
    private bool _disposed;

    /// <summary>
    /// Gets a value indicating whether a programmatic scroll animation is in progress.
    /// </summary>
    public bool IsScrollingProgrammatically { get; private set; }

    /// <summary>
    /// Registers a section anchor in top-to-bottom visual order.
    /// </summary>
    /// <param name="key">The section identifier.</param>
    /// <param name="control">The section anchor control.</param>
    public void RegisterSection(TKey key, Control control)
    {
        _sections.Add((key, control));
    }

    /// <summary>
    /// Subscribes to scroll tracking on the scroll viewer.
    /// </summary>
    public void Attach()
    {
        DetachHandlers();
        scrollViewer.ScrollChanged += OnScrollChanged;
        scrollViewer.PointerWheelChanged += OnPointerWheelChanged;
    }

    /// <summary>
    /// Unsubscribes from scroll tracking and stops any animation in progress.
    /// </summary>
    public void Detach()
    {
        DetachHandlers();
        StopAnimation();
    }

    /// <summary>
    /// Animates the scroll viewer so the target control top aligns with the viewport top.
    /// </summary>
    /// <param name="targetControl">The target control to scroll to.</param>
    public void ScrollToControl(Control targetControl)
    {
        ScrollToControl(targetControl, default);
    }

    /// <summary>
    /// Animates the scroll viewer so the section top aligns with the viewport top.
    /// </summary>
    /// <param name="key">The section identifier.</param>
    public void ScrollToSection(TKey key)
    {
        var targetControl = FindControl(key);
        if (targetControl is not null)
        {
            ScrollToControl(targetControl, (true, key));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
    }

    private static double EaseInOutQuadratic(double progress)
    {
        return progress < 0.5
            ? 2.0 * progress * progress
            : 1.0 - (Math.Pow((-2.0 * progress) + 2.0, 2) / 2.0);
    }

    private void ScrollToControl(Control targetControl, (bool HasValue, TKey Value) explicitKey)
    {
        if (targetControl is null || scrollViewer.Content is not Control content)
        {
            return;
        }

        try
        {
            var transform = targetControl.TransformToVisual(content);
            if (!transform.HasValue)
            {
                return;
            }

            var position = transform.Value.Transform(new Point(0, 0));
            var targetKey = explicitKey;
            if (!targetKey.HasValue)
            {
                foreach (var (key, control) in _sections)
                {
                    if (ReferenceEquals(control, targetControl))
                    {
                        targetKey = (true, key);
                        break;
                    }
                }
            }

            StartAnimation(Math.Max(0, position.Y), targetControl, targetKey);
        }
        catch (InvalidOperationException)
        {
            // Visual target is detached from visual tree; ignore transform calculation.
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (IsScrollingProgrammatically)
        {
            return;
        }

        UpdateActiveSection();
    }

    private void UpdateActiveSection()
    {
        if (_sections.Count == 0)
        {
            return;
        }

        if (TryApplyBottomSnap())
        {
            return;
        }

        var activeKey = FindActiveKey();
        ReportActiveKey(activeKey is not null ? activeKey : _sections[0].Key);
    }

    private bool TryApplyBottomSnap()
    {
        var maxScrollY = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
        var isAtBottom = maxScrollY > 0 && scrollViewer.Offset.Y >= maxScrollY - ScrollSpyConstants.BottomSnapTolerance;
        if (!isAtBottom)
        {
            return false;
        }

        ReportActiveKey(_sections[^1].Key);
        return true;
    }

    private TKey? FindActiveKey()
    {
        var threshold = Math.Max(ScrollSpyConstants.MinActiveThreshold, scrollViewer.Viewport.Height * ScrollSpyConstants.ViewportThresholdRatio);
        TKey? activeKey = default;
        foreach (var (key, control) in _sections)
        {
            if (IsAboveThreshold(control, threshold))
            {
                activeKey = key;
            }
        }

        return activeKey;
    }

    private bool IsAboveThreshold(Control control, double threshold)
    {
        try
        {
            var transform = control.TransformToVisual(scrollViewer);
            if (!transform.HasValue)
            {
                return false;
            }

            return transform.Value.Transform(new Point(0, 0)).Y <= threshold;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ReportActiveKey(TKey key)
    {
        if (_hasReportedKey && EqualityComparer<TKey>.Default.Equals(_lastReportedKey, key))
        {
            return;
        }

        _hasReportedKey = true;
        _lastReportedKey = key;
        activeSectionChanged(key);
    }

    private Control? FindControl(TKey key)
    {
        foreach (var (candidateKey, control) in _sections)
        {
            if (EqualityComparer<TKey>.Default.Equals(candidateKey, key))
            {
                return control;
            }
        }

        return null;
    }

    private void StartAnimation(double initialTargetY, Control? targetControl = null, (bool HasValue, TKey Value) targetKey = default)
    {
        StopAnimationTimer();

        _animationGeneration++;
        _animTargetKey = targetKey;

        var currentY = scrollViewer.Offset.Y;
        var maxScrollY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var effectiveTargetY = Math.Clamp(initialTargetY, 0, maxScrollY);

        if (Math.Abs(currentY - effectiveTargetY) < ScrollSpyConstants.ScrollSnapEpsilon && initialTargetY <= maxScrollY)
        {
            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, effectiveTargetY);
            IsScrollingProgrammatically = false;
            if (targetKey.HasValue)
            {
                ReportActiveKey(targetKey.Value!);
            }
            else
            {
                UpdateActiveSection();
            }

            return;
        }

        IsScrollingProgrammatically = true;
        _animTargetControl = targetControl;
        _animStartOffset = currentY;
        _animTargetOffset = initialTargetY;
        _animStartTime = DateTime.UtcNow;

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ScrollSpyConstants.AnimationFrameIntervalMs) };
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

        _animTargetControl = null;
    }

    private void StopAnimation()
    {
        _animationGeneration++;
        _animTargetKey = default;
        StopAnimationTimer();
        IsScrollingProgrammatically = false;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_animTargetControl is not null && scrollViewer.Content is Control content)
        {
            try
            {
                var transform = _animTargetControl.TransformToVisual(content);
                if (transform.HasValue)
                {
                    _animTargetOffset = Math.Max(0, transform.Value.Transform(new Point(0, 0)).Y);
                }
            }
            catch (InvalidOperationException)
            {
                // Visual target is detached during animation; retain current target offset.
            }
        }

        var maxScrollY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var targetY = Math.Clamp(_animTargetOffset, 0, maxScrollY);

        var elapsed = DateTime.UtcNow - _animStartTime;
        var progress = Math.Min(1.0, elapsed.TotalMilliseconds / _animationDuration.TotalMilliseconds);
        var currentY = _animStartOffset + ((targetY - _animStartOffset) * EaseInOutQuadratic(progress));
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, currentY);

        if (progress >= 1.0)
        {
            var gen = _animationGeneration;
            var targetKey = _animTargetKey;
            StopAnimationTimer();
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (!_disposed && _animationGeneration == gen)
                    {
                        IsScrollingProgrammatically = false;
                        if (targetKey.HasValue)
                        {
                            ReportActiveKey(targetKey.Value!);
                        }
                        else
                        {
                            UpdateActiveSection();
                        }
                    }
                },
                DispatcherPriority.Normal);
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (IsScrollingProgrammatically)
        {
            StopAnimation();
        }
    }

    private void DetachHandlers()
    {
        scrollViewer.ScrollChanged -= OnScrollChanged;
        scrollViewer.PointerWheelChanged -= OnPointerWheelChanged;
    }
}
