using Avalonia;
using Avalonia.Controls;
using GenHub.Core.Constants;
using System;

namespace GenHub.Common.Controls;

/// <summary>
/// Panel arranging children in two columns with independent row heights.
/// Unlike a uniform grid, a tall child does not stretch its neighbors.
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

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double columnWidth = ColumnWidth(availableSize.Width, true);
        double[] heights = [0, 0];
        bool[] started = [false, false];

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            int column = i % 2;
            if (started[column])
            {
                heights[column] += RowSpacing;
            }

            heights[column] += child.DesiredSize.Height;
            started[column] = true;
        }

        double width = double.IsInfinity(availableSize.Width) ? (columnWidth * 2) + ColumnSpacing : availableSize.Width;
        return new Size(width, Math.Max(heights[0], heights[1]));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        double columnWidth = ColumnWidth(finalSize.Width, false);
        double[] tops = [0, 0];

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            int column = i % 2;
            double left = column == 0 ? 0 : columnWidth + ColumnSpacing;
            double height = child.DesiredSize.Height;
            child.Arrange(new Rect(left, tops[column], columnWidth, height));
            tops[column] += height + RowSpacing;
        }

        return finalSize;
    }

    private double ColumnWidth(double availableWidth, bool measure)
    {
        if (!double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            return Math.Max((availableWidth - ColumnSpacing) / 2, 0);
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
