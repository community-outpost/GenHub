using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A STATUS or STYLE flag value. Recognized flags round-trip in engine canonical
/// order while unrecognized tokens are preserved verbatim in first-seen order.
/// </summary>
public sealed record WndStatusValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WndStatusValue"/> class.
    /// </summary>
    /// <param name="flags">The recognized flags.</param>
    /// <param name="unknownTokens">The unrecognized tokens in first-seen order.</param>
    public WndStatusValue(IReadOnlyCollection<string> flags, IReadOnlyList<string> unknownTokens)
    {
        Flags = flags;
        UnknownTokens = unknownTokens;
    }

    /// <summary>
    /// Gets the recognized flags.
    /// </summary>
    public IReadOnlyCollection<string> Flags { get; }

    /// <summary>
    /// Gets the unrecognized tokens in first-seen order.
    /// </summary>
    public IReadOnlyList<string> UnknownTokens { get; }

    /// <summary>
    /// Gets a value indicating whether any flag is set.
    /// </summary>
    public bool HasAny => Flags.Count > 0 || UnknownTokens.Count > 0;

    /// <summary>
    /// Parses a STATUS property value against the engine status table.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <returns>The parsed value, never null.</returns>
    public static WndStatusValue ParseStatus(string? value)
    {
        return Parse(value, WndConstants.StatusFlags.All);
    }

    /// <summary>
    /// Parses a STYLE property value against the engine style table.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <returns>The parsed value, never null.</returns>
    public static WndStatusValue ParseStyle(string? value)
    {
        return Parse(value, WndConstants.StyleTypes.All);
    }

    /// <summary>
    /// Returns the canonical flag representation, or NULL when empty.
    /// The caller supplies the canonical order so status and style emit correctly.
    /// </summary>
    /// <param name="canonicalOrder">The canonical flag order.</param>
    /// <returns>The canonical representation.</returns>
    public string ToString(IReadOnlyList<string> canonicalOrder)
    {
        var ordered = canonicalOrder.Where(flag => Flags.Contains(flag, StringComparer.OrdinalIgnoreCase));
        var tokens = ordered.Concat(UnknownTokens).ToList();
        return tokens.Count == 0 ? WndConstants.Syntax.NullFlags : string.Join(WndConstants.Syntax.FlagSeparator, tokens);
    }

    private static WndStatusValue Parse(string? value, IReadOnlyList<string> knownFlags)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return new WndStatusValue(flags, unknown);
        }

        foreach (var token in value.Split(WndConstants.Syntax.FlagSeparator))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0
                || string.Equals(trimmed, WndConstants.Syntax.NullFlags, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, WndConstants.Syntax.NoneFlags, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (knownFlags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                flags.Add(trimmed);
            }
            else if (!unknown.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                unknown.Add(trimmed);
            }
        }

        return new WndStatusValue(flags, unknown);
    }
}
