using GenHub.Core.Constants;
using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A LISTBOXDATA property value.
/// </summary>
public sealed record WndListboxData
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndListboxData"/> class.
    /// </summary>
    /// <param name="length">The maximum number of entries.</param>
    /// <param name="autoScroll">Whether the list scrolls to new entries.</param>
    /// <param name="scrollIfAtEnd">The optional scroll-if-at-end flag, or null when absent.</param>
    /// <param name="autoPurge">Whether old entries are purged automatically.</param>
    /// <param name="scrollBar">Whether a scroll bar is shown.</param>
    /// <param name="multiSelect">Whether multiple entries can be selected.</param>
    /// <param name="columns">The column count.</param>
    /// <param name="columnWidths">The column width percentages.</param>
    /// <param name="forceSelect">Whether an entry is always selected.</param>
    public WndListboxData(
        int length,
        bool autoScroll,
        bool? scrollIfAtEnd,
        bool autoPurge,
        bool scrollBar,
        bool multiSelect,
        int columns,
        IReadOnlyList<int> columnWidths,
        bool forceSelect)
    {
        Length = length;
        AutoScroll = autoScroll;
        ScrollIfAtEnd = scrollIfAtEnd;
        AutoPurge = autoPurge;
        ScrollBar = scrollBar;
        MultiSelect = multiSelect;
        Columns = columns;
        ColumnWidths = columnWidths;
        ForceSelect = forceSelect;
    }

    /// <summary>
    /// Gets the maximum number of entries.
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// Gets a value indicating whether the list scrolls to new entries.
    /// </summary>
    public bool AutoScroll { get; }

    /// <summary>
    /// Gets the optional scroll-if-at-end flag, or null when absent.
    /// </summary>
    public bool? ScrollIfAtEnd { get; }

    /// <summary>
    /// Gets a value indicating whether old entries are purged automatically.
    /// </summary>
    public bool AutoPurge { get; }

    /// <summary>
    /// Gets a value indicating whether a scroll bar is shown.
    /// </summary>
    public bool ScrollBar { get; }

    /// <summary>
    /// Gets a value indicating whether multiple entries can be selected.
    /// </summary>
    public bool MultiSelect { get; }

    /// <summary>
    /// Gets the column count.
    /// </summary>
    public int Columns { get; }

    /// <summary>
    /// Gets the column width percentages.
    /// </summary>
    public IReadOnlyList<int> ColumnWidths { get; }

    /// <summary>
    /// Gets a value indicating whether an entry is always selected.
    /// </summary>
    public bool ForceSelect { get; }

    /// <summary>
    /// Tries to parse a LISTBOXDATA property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="data">The parsed data when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndListboxData? data)
    {
        data = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        var reader = new TokenReader(tokens);
        if (!reader.TakeLabel(WndConstants.GadgetDataKeys.Length)
            || !reader.TakeInt(out var length)
            || !reader.TakeLabel(WndConstants.GadgetDataKeys.AutoScroll)
            || !reader.TakeBool(out var autoScroll))
        {
            return false;
        }

        var scrollIfAtEnd = reader.TakeOptionalBool(WndConstants.GadgetDataKeys.ScrollIfAtEnd);
        if (!reader.TakeLabel(WndConstants.GadgetDataKeys.AutoPurge)
            || !reader.TakeBool(out var autoPurge)
            || !reader.TakeLabel(WndConstants.GadgetDataKeys.ScrollBar)
            || !reader.TakeBool(out var scrollBar)
            || !reader.TakeLabel(WndConstants.GadgetDataKeys.MultiSelect)
            || !reader.TakeBool(out var multiSelect)
            || !reader.TakeLabel(WndConstants.GadgetDataKeys.Columns)
            || !reader.TakeInt(out var columns)
            || columns < 0)
        {
            return false;
        }

        var widths = new List<int>(Math.Max(columns, 0));
        if (columns > 1)
        {
            for (var i = 0; i < columns; i++)
            {
                if (!reader.TakeLabel(WndConstants.GadgetDataKeys.Columns) || !reader.TakeInt(out var width))
                {
                    return false;
                }

                widths.Add(width);
            }
        }

        if (!reader.TakeLabel(WndConstants.GadgetDataKeys.ForceSelect)
            || !reader.TakeBool(out var forceSelect)
            || !reader.AtEnd)
        {
            return false;
        }

        data = new WndListboxData(length, autoScroll, scrollIfAtEnd, autoPurge, scrollBar, multiSelect, columns, widths, forceSelect);
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
            WndValueFormatter.Pair(WndConstants.GadgetDataKeys.Length, Length.ToString()),
            WndValueFormatter.Pair(WndConstants.GadgetDataKeys.AutoScroll, WndValueTokenizer.FormatBool(AutoScroll)),
        };
        if (ScrollIfAtEnd.HasValue)
        {
            parts.Add(WndValueFormatter.Pair(WndConstants.GadgetDataKeys.ScrollIfAtEnd, WndValueTokenizer.FormatBool(ScrollIfAtEnd.Value)));
        }

        parts.Add(WndValueFormatter.Pair(WndConstants.GadgetDataKeys.AutoPurge, WndValueTokenizer.FormatBool(AutoPurge)));
        parts.Add(WndValueFormatter.Pair(WndConstants.GadgetDataKeys.ScrollBar, WndValueTokenizer.FormatBool(ScrollBar)));
        parts.Add(WndValueFormatter.Pair(WndConstants.GadgetDataKeys.MultiSelect, WndValueTokenizer.FormatBool(MultiSelect)));
        parts.Add(WndValueFormatter.Pair(WndConstants.GadgetDataKeys.Columns, Columns.ToString()));
        if (Columns > 1)
        {
            foreach (var width in ColumnWidths)
            {
                parts.Add(WndValueFormatter.Pair(WndConstants.GadgetDataKeys.Columns, width.ToString()));
            }
        }

        parts.Add(WndValueFormatter.Pair(WndConstants.GadgetDataKeys.ForceSelect, WndValueTokenizer.FormatBool(ForceSelect)));
        return WndValueFormatter.JoinParts(parts);
    }

    private sealed class TokenReader
    {
        private readonly IReadOnlyList<string> _tokens;
        private int _index;

        public TokenReader(IReadOnlyList<string> tokens)
        {
            _tokens = tokens;
        }

        public bool AtEnd => _index >= _tokens.Count;

        public bool TakeLabel(string expected)
        {
            if (_index >= _tokens.Count || !string.Equals(_tokens[_index], expected, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _index++;
            return true;
        }

        public bool TakeInt(out int value)
        {
            value = 0;
            if (_index >= _tokens.Count || !int.TryParse(_tokens[_index], out value))
            {
                return false;
            }

            _index++;
            return true;
        }

        public bool TakeBool(out bool value)
        {
            value = false;
            if (_index >= _tokens.Count || !WndValueTokenizer.TryParseBool(_tokens[_index], out value))
            {
                return false;
            }

            _index++;
            return true;
        }

        public bool? TakeOptionalBool(string label)
        {
            if (_index + 1 >= _tokens.Count
                || !string.Equals(_tokens[_index], label, StringComparison.OrdinalIgnoreCase)
                || !WndValueTokenizer.TryParseBool(_tokens[_index + 1], out var value))
            {
                return null;
            }

            _index += 2;
            return value;
        }
    }
}
