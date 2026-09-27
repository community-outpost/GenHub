using GenHub.Core.Constants;
using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A TABCONTROLDATA property value.
/// </summary>
public sealed record WndTabControlData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndTabControlData"/> class.
    /// </summary>
    /// <param name="tabOrientation">The tab orientation.</param>
    /// <param name="tabEdge">The tab edge.</param>
    /// <param name="tabWidth">The tab width.</param>
    /// <param name="tabHeight">The tab height.</param>
    /// <param name="tabCount">The tab count.</param>
    /// <param name="paneBorder">The pane border.</param>
    /// <param name="paneDisabled">The per-pane disabled flags.</param>
    public WndTabControlData(
        int tabOrientation,
        int tabEdge,
        int tabWidth,
        int tabHeight,
        int tabCount,
        int paneBorder,
        IReadOnlyList<bool> paneDisabled)
    {
        ArgumentNullException.ThrowIfNull(paneDisabled);
        if (paneDisabled.Count != tabCount)
        {
            throw new ArgumentException(
                $"PaneDisabled count ({paneDisabled.Count}) must match tab count ({tabCount}).",
                nameof(paneDisabled));
        }

        TabOrientation = tabOrientation;
        TabEdge = tabEdge;
        TabWidth = tabWidth;
        TabHeight = tabHeight;
        TabCount = tabCount;
        PaneBorder = paneBorder;
        PaneDisabled = paneDisabled;
    }

    /// <summary>
    /// Gets the tab orientation.
    /// </summary>
    public int TabOrientation { get; }

    /// <summary>
    /// Gets the tab edge.
    /// </summary>
    public int TabEdge { get; }

    /// <summary>
    /// Gets the tab width.
    /// </summary>
    public int TabWidth { get; }

    /// <summary>
    /// Gets the tab height.
    /// </summary>
    public int TabHeight { get; }

    /// <summary>
    /// Gets the tab count.
    /// </summary>
    public int TabCount { get; }

    /// <summary>
    /// Gets the pane border.
    /// </summary>
    public int PaneBorder { get; }

    /// <summary>
    /// Gets the per-pane disabled flags.
    /// </summary>
    public IReadOnlyList<bool> PaneDisabled { get; }

    /// <summary>
    /// Tries to parse a TABCONTROLDATA property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="data">The parsed data when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndTabControlData? data)
    {
        data = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        var index = 0;
        if (!TakeInt(tokens, ref index, WndConstants.GadgetDataKeys.TabOrientation, out var orientation)
            || !TakeInt(tokens, ref index, WndConstants.GadgetDataKeys.TabEdge, out var edge)
            || !TakeInt(tokens, ref index, WndConstants.GadgetDataKeys.TabWidth, out var width)
            || !TakeInt(tokens, ref index, WndConstants.GadgetDataKeys.TabHeight, out var height)
            || !TakeInt(tokens, ref index, WndConstants.GadgetDataKeys.TabCount, out var count)
            || !TakeInt(tokens, ref index, WndConstants.GadgetDataKeys.PaneBorder, out var border))
        {
            return false;
        }

        if (index >= tokens.Count
            || !string.Equals(tokens[index], WndConstants.GadgetDataKeys.PaneDisabled, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        index++;
        if (index >= tokens.Count || !int.TryParse(tokens[index], out var disabledCount) || disabledCount < 0)
        {
            return false;
        }

        index++;
        var disabled = new List<bool>(disabledCount);
        for (var i = 0; i < disabledCount; i++)
        {
            if (index >= tokens.Count || !WndValueTokenizer.TryParseBool(tokens[index], out var flag))
            {
                return false;
            }

            disabled.Add(flag);
            index++;
        }

        if (index != tokens.Count || disabledCount != count)
        {
            return false;
        }

        data = new WndTabControlData(orientation, edge, width, height, count, border, disabled);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this data.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        var parts = new List<string>
        {
            WndValueFormatter.Pair(WndConstants.GadgetDataKeys.TabOrientation, TabOrientation.ToString()),
            WndValueFormatter.Pair(WndConstants.GadgetDataKeys.TabEdge, TabEdge.ToString()),
            WndValueFormatter.Pair(WndConstants.GadgetDataKeys.TabWidth, TabWidth.ToString()),
            WndValueFormatter.Pair(WndConstants.GadgetDataKeys.TabHeight, TabHeight.ToString()),
            WndValueFormatter.Pair(WndConstants.GadgetDataKeys.TabCount, TabCount.ToString()),
            WndValueFormatter.Pair(WndConstants.GadgetDataKeys.PaneBorder, PaneBorder.ToString()),
            WndValueFormatter.Pair(WndConstants.GadgetDataKeys.PaneDisabled, PaneDisabled.Count.ToString()),
        };
        foreach (var flag in PaneDisabled)
        {
            parts.Add(WndValueTokenizer.FormatBool(flag));
        }

        return WndValueFormatter.JoinParts(parts);
    }

    private static bool TakeInt(IReadOnlyList<string> tokens, ref int index, string label, out int value)
    {
        value = 0;
        if (index + 1 >= tokens.Count
            || !string.Equals(tokens[index], label, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(tokens[index + 1], out value))
        {
            return false;
        }

        index += 2;
        return true;
    }
}
