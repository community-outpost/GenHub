using Avalonia;
using Avalonia.Controls;
using GenHub.Core.Constants;
using System;

namespace GenHub.Common.Controls;

/// <summary>
/// Panel arranging children in a responsive flex grid with independent row heights.
/// Unlike a uniform grid, a tall child does not stretch its neighbors, and the
/// column count shrinks as the panel narrows so cards never squeeze below
/// <see cref="MinColumnWidth"/>.
/// </summary>
public sealed class TwoColumnPanel : Panel
{
    /// <summary>
    /// The horizontal gap between columns.
    /// </summary>
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<TwoColumnPanel, double>(nameof(ColumnSpacing), defaultValue: 6.0);

    /// <summary>
    /// The vertical gap between rows.
    /// </summary>
    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<TwoColumnPanel, double>(nameof(RowSpacing), defaultValue: 2.0);

    /// <summary>
    /// The minimum column width before the grid drops a column.
    /// </summary>
    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<TwoColumnPanel, double>(nameof(MinColumnWidth), defaultValue: EditorConstants.TwoColumnDefaultWidth);

    /// <summary>
    /// The maximum column count on wide layouts.
    /// </summary>
    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<TwoColumnPanel, int>(nameof(MaxColumns), defaultValue: 2);

    static TwoColumnPanel()
    {
        AffectsMeasure<TwoColumnPanel>(ColumnSpacingProperty, RowSpacingProperty, MinColumnWidthProperty, MaxColumnsProperty);
    }

    /// <summary>
    /// Gets or sets the horizontal gap between columns.
    /// </summary>
    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical gap between rows.
    /// </summary>
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum column width before the grid drops a column.
    /// </summary>
    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum column count on wide layouts.
    /// </summary>
    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        int columns = ColumnCount(availableSize.Width);
        double columnWidth = ColumnWidth(availableSize.Width, columns, true);
        var heights = new double[columns];
        var started = new bool[columns];

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            int column = i % columns;
            if (started[column])
            {
                heights[column] += RowSpacing;
            }

            heights[column] += child.DesiredSize.Height;
            started[column] = true;
        }

        double width = double.IsInfinity(availableSize.Width)
            ? (columnWidth * columns) + (ColumnSpacing * (columns - 1))
            : availableSize.Width;
        double height = 0;
        foreach (var columnHeight in heights)
        {
            height = Math.Max(height, columnHeight);
        }

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        int columns = ColumnCount(finalSize.Width);
        double columnWidth = ColumnWidth(finalSize.Width, columns, false);
        var tops = new double[columns];

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            int column = i % columns;
            double left = column * (columnWidth + ColumnSpacing);
            double height = child.DesiredSize.Height;
            child.Arrange(new Rect(left, tops[column], columnWidth, height));
            tops[column] += height + RowSpacing;
        }

        return finalSize;
    }

    private int ColumnCount(double availableWidth)
    {
        int max = Math.Max(MaxColumns, 1);
        double min = MinColumnWidth;
        if (double.IsInfinity(availableWidth) || availableWidth <= 0 || min <= 0 || !double.IsFinite(min))
        {
            return max;
        }

        int fit = (int)Math.Floor((availableWidth + ColumnSpacing) / (min + ColumnSpacing));
        return Math.Clamp(fit, 1, max);
    }

    private double ColumnWidth(double availableWidth, int columns, bool measure)
    {
        if (!double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            return Math.Max((availableWidth - (ColumnSpacing * (columns - 1))) / columns, 0);
        }

        if (!measure)
        {
            return EditorConstants.TwoColumnDefaultWidth;
        }

        double widest = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(Size.Infinity);
            widest = Math.Max(widest, child.DesiredSize.Width);
        }

        return widest > 0 ? widest : EditorConstants.TwoColumnDefaultWidth;
    }
}
