using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A full draw data block: nine image/color/border entries.
/// </summary>
public sealed record WndDrawDataSet
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndDrawDataSet"/> class.
    /// </summary>
    /// <param name="entries">The entries in file order.</param>
    public WndDrawDataSet(IReadOnlyList<WndDrawDataEntry> entries)
    {
        Entries = entries;
    }

    /// <summary>
    /// Gets the entries in file order.
    /// </summary>
    public IReadOnlyList<WndDrawDataEntry> Entries { get; }

    /// <summary>
    /// Gets an empty nine-entry set padded with empty entries.
    /// </summary>
    public static WndDrawDataSet Empty => new(
        Enumerable.Repeat(WndDrawDataEntry.Empty, WndConstants.DrawData.EntryCount).ToArray());

    /// <summary>
    /// Tries to parse a draw data property value. Accepts one or more 12-token entries.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="set">The parsed set when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndDrawDataSet? set)
    {
        set = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        if (tokens.Count == 0 || tokens.Count % WndConstants.DrawData.TokensPerEntry != 0)
        {
            return false;
        }

        var entryCount = tokens.Count / WndConstants.DrawData.TokensPerEntry;
        var entries = new List<WndDrawDataEntry>(entryCount);
        var index = 0;
        while (index < tokens.Count)
        {
            if (!WndDrawDataEntry.TryParseTokens(tokens, index, out var entry) || entry == null)
            {
                return false;
            }

            entries.Add(entry);
            index += WndConstants.DrawData.TokensPerEntry;
        }

        set = new WndDrawDataSet(entries);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this set, padded to nine entries.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        var entries = Entries.ToList();
        while (entries.Count < WndConstants.DrawData.EntryCount)
        {
            entries.Add(WndDrawDataEntry.Empty);
        }

        return string.Join(
            WndConstants.Syntax.ComponentListSeparator,
            entries.Select(entry => entry.ToString()));
    }
}
