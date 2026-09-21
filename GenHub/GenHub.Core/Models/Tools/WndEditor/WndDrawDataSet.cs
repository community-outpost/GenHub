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
    /// Gets an empty nine-entry set.
    /// </summary>
    public static WndDrawDataSet Empty
    {
        get
        {
            var entries = new WndDrawDataEntry[WndConstants.DrawData.EntryCount];
            Array.Fill(entries, WndDrawDataEntry.Empty);
            return new WndDrawDataSet(entries);
        }
    }

    /// <summary>
    /// Tries to parse a draw data property value.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <param name="set">The parsed set when successful.</param>
    /// <returns>True when the value parsed successfully.</returns>
    public static bool TryParse(string? value, out WndDrawDataSet? set)
    {
        set = null;
        var tokens = WndValueTokenizer.SplitTokens(value);
        var entries = new List<WndDrawDataEntry>(WndConstants.DrawData.EntryCount);
        var index = 0;
        while (index < tokens.Count)
        {
            if (!WndDrawDataEntry.TryParseTokens(tokens, index, out var entry) || entry == null)
            {
                return false;
            }

            entries.Add(entry);
            index += 12;
        }

        if (entries.Count != WndConstants.DrawData.EntryCount)
        {
            return false;
        }

        set = new WndDrawDataSet(entries);
        return true;
    }

    /// <summary>
    /// Returns the canonical single-line representation of this set.
    /// </summary>
    /// <returns>The canonical representation.</returns>
    public override string ToString()
    {
        return string.Join(
            WndConstants.Syntax.ComponentListSeparator,
            Entries.Select(entry => entry.ToString()));
    }
}
