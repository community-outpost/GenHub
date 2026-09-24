using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;

namespace GenHub.Common.Controls;

/// <summary>
/// Attached behavior that enables Blender-style click-and-drag horizontal scrubbing
/// on <see cref="NumericUpDown"/> controls across the GenHub ecosystem.
/// </summary>
public static class NumericScrubber
{
    /// <summary>
    /// Attached property enabling horizontal drag-scrubbing on a NumericUpDown control.
    /// </summary>
    public static readonly AttachedProperty<bool> EnableScrubberProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, bool>(
            "EnableScrubber",
            typeof(NumericScrubber),
            defaultValue: false);

    private const double DragThresholdPixels = 4.0;
    private const double PixelsPerStep = 6.0;

    private static readonly AttachedProperty<ScrubberState?> StateProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, ScrubberState?>(
            "State",
            typeof(NumericScrubber),
            defaultValue: null);

    static NumericScrubber()
    {
        EnableScrubberProperty.Changed.AddClassHandler<NumericUpDown>(OnEnableScrubberChanged);
    }

    /// <summary>
    /// Gets a value indicating whether the scrubber is enabled on the control.
    /// </summary>
    /// <param name="element">The numeric up-down control to inspect.</param>
    /// <returns><c>true</c> if scrubber is enabled; otherwise, <c>false</c>.</returns>
    public static bool GetEnableScrubber(NumericUpDown element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(EnableScrubberProperty);
    }

    /// <summary>
    /// Sets a value indicating whether the scrubber is enabled on the control.
    /// </summary>
    /// <param name="element">The numeric up-down control to configure.</param>
    /// <param name="value">The enabled state.</param>
    public static void SetEnableScrubber(NumericUpDown element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EnableScrubberProperty, value);
    }

    private static void OnEnableScrubberChanged(NumericUpDown control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            Attach(control);
        }
        else
        {
            Detach(control);
        }
    }

    private static void Attach(NumericUpDown control)
    {
        var existing = control.GetValue(StateProperty);
        if (existing != null)
        {
            return;
        }

        var state = new ScrubberState(control);
        control.SetValue(StateProperty, state);
        state.AttachHandlers();
    }

    private static void Detach(NumericUpDown control)
    {
        var state = control.GetValue(StateProperty);
        if (state != null)
        {
            state.DetachHandlers();
            control.SetValue(StateProperty, null);
        }
    }

    private sealed class ScrubberState(NumericUpDown control)
    {
        private static Cursor? _dragCursor;

        private static Cursor DragCursor => _dragCursor ??= new Cursor(StandardCursorType.SizeWestEast);

        private Point _startPoint;
        private decimal _startValue;
        private bool _isPressed;
        private bool _isDragging;

        public void AttachHandlers()
        {
            control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Tunnel);
        }

        public void DetachHandlers()
        {
            control.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            control.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
            control.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
            control.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
            Reset(null);
        }

        private static bool IsInsideSpinnerButton(Visual visual)
        {
            var current = visual;
            while (current != null)
            {
                if (current is ButtonSpinner or RepeatButton)
                {
                    return true;
                }

                current = current.GetVisualParent();
            }

            return false;
        }

        private static int GetDecimalPlaces(decimal value)
        {
            var bits = decimal.GetBits(value);
            return (bits[3] >> 16) & 0x7F;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                return;
            }

            // Do not intercept if clicking on the spinner buttons (ButtonSpinner, RepeatButton, etc.)
            if (e.Source is Visual visual && IsInsideSpinnerButton(visual))
            {
                return;
            }

            // Do not arm scrubbing if the inner text box already has focus
            var textBox = control.FindDescendantOfType<TextBox>();
            if (textBox != null && textBox.IsFocused)
            {
                return;
            }

            _startPoint = e.GetPosition(control);
            _startValue = control.Value ?? 0m;
            _isPressed = true;
            _isDragging = false;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isPressed)
            {
                return;
            }

            var currentPoint = e.GetPosition(control);
            var deltaX = currentPoint.X - _startPoint.X;

            if (!_isDragging && Math.Abs(deltaX) >= DragThresholdPixels)
            {
                _isDragging = true;
                e.Pointer.Capture(control);
                control.Cursor = DragCursor;
                control.Focus();
            }

            if (_isDragging)
            {
                e.Handled = true;

                var step = control.Increment > 0 ? control.Increment : 1m;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    // Blender-style fine control with Shift
                    step *= 0.1m;
                }
                else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    // Snapped / accelerated control with Ctrl
                    step *= 10m;
                }

                var stepsCount = (decimal)(deltaX / PixelsPerStep);
                var deltaValue = stepsCount * step;
                var targetValue = _startValue + deltaValue;

                if (targetValue < control.Minimum)
                {
                    targetValue = control.Minimum;
                }

                if (targetValue > control.Maximum)
                {
                    targetValue = control.Maximum;
                }

                // Snap to decimal precision of step if appropriate
                var decimals = GetDecimalPlaces(step);
                control.Value = Math.Round(targetValue, decimals);
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isPressed)
            {
                return;
            }

            if (_isDragging)
            {
                e.Handled = true;
                Reset(e.Pointer);
            }
            else
            {
                // Click without drag: enter direct text editing mode if not already focused
                _isPressed = false;
                var textBox = control.FindDescendantOfType<TextBox>();
                if (textBox != null && !textBox.IsFocused)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            }
        }

        private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            Reset(null);
        }

        private void Reset(IPointer? pointer)
        {
            if (_isDragging && pointer != null)
            {
                pointer.Capture(null);
            }

            _isPressed = false;
            _isDragging = false;
            control.Cursor = null;
        }
    }
}
