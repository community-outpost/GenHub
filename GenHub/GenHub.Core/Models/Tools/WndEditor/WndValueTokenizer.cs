using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Text;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Tokenizes window definition property values the way the engine does.
/// The engine splits values on spaces, colons, commas, and newlines, so labels
/// and values arrive as a flat token stream (see GameWindowManagerScript.cpp).
/// Quoted spans are kept together for names containing spaces.
/// </summary>
public static class WndValueTokenizer
{
    /// <summary>
    /// Splits a raw property value into engine-style tokens.
    /// </summary>
    /// <param name="value">The raw property value.</param>
    /// <returns>The token list, never null.</returns>
    public static List<string> SplitTokens(string? value)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return tokens;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in value)
        {
            if (ch == WndConstants.Syntax.Quote)
            {
                inQuotes = !inQuotes;
                current.Append(ch);
                continue;
            }

            if (!inQuotes && IsEngineSeparator(ch))
            {
                FlushCurrent(current, tokens);
                continue;
            }

            current.Append(ch);
        }

        FlushCurrent(current, tokens);
        return tokens;
    }

    /// <summary>
    /// Formats a boolean in engine spelling.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>1 or 0.</returns>
    public static string FormatBool(bool value)
    {
        return value ? WndConstants.Syntax.BoolTrue : WndConstants.Syntax.BoolFalse;
    }

    /// <summary>
    /// Tries to read an engine boolean token.
    /// </summary>
    /// <param name="token">The token to read.</param>
    /// <param name="value">The parsed value when successful.</param>
    /// <returns>True when the token is a recognized boolean.</returns>
    public static bool TryParseBool(string? token, out bool value)
    {
        if (string.Equals(token, WndConstants.Syntax.BoolTrue, StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(token, WndConstants.Syntax.BoolFalse, StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    /// <summary>
    /// Removes surrounding double quotes from a token when present.
    /// </summary>
    /// <param name="token">The token to unquote.</param>
    /// <returns>The unquoted token.</returns>
    public static string Unquote(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Length >= 2
            && trimmed[0] == WndConstants.Syntax.Quote
            && trimmed[^1] == WndConstants.Syntax.Quote)
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    /// <summary>
    /// Adds double quotes around a literal.
    /// </summary>
    /// <param name="literal">The literal to quote.</param>
    /// <returns>The quoted literal.</returns>
    public static string Quote(string literal)
    {
        return string.Concat(WndConstants.Syntax.Quote, literal, WndConstants.Syntax.Quote);
    }

    private static bool IsEngineSeparator(char ch)
    {
        return ch == ' '
            || ch == '\t'
            || ch == '\r'
            || ch == '\n'
            || ch == WndConstants.Syntax.CoordinateSeparator
            || ch == WndConstants.Syntax.ComponentSeparator;
    }

    private static void FlushCurrent(StringBuilder current, List<string> tokens)
    {
        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
            current.Clear();
        }
    }
}
