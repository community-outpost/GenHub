using GenHub.Core.Models.Tools.RmlEditor;
using System;
using System.Collections.Generic;

namespace GenHub.Core.Services.Tools.RmlEditor;

/// <summary>
/// Matches style sheet selectors against interface elements and cascades declarations.
/// Only structural selectors apply to the static preview: any selector carrying a
/// pseudo-class or pseudo-element is skipped because its state is unknown.
/// </summary>
public static class RmlStyleComputer
{
    private enum SelectorCombinator
    {
        Descendant,
        Child,
    }

    private sealed record SelectorPart(string Compound, SelectorCombinator Combinator);

    private const int IdSpecificity = 100;
    private const int ClassSpecificity = 10;
    private const int TypeSpecificity = 1;

    /// <summary>
    /// Computes the cascaded style of an element from ordered style sheets and inline declarations.
    /// </summary>
    /// <param name="element">The element to style.</param>
    /// <param name="ancestors">The element ancestors, immediate parent first.</param>
    /// <param name="styleSheets">The style sheets in cascade order.</param>
    /// <param name="inlineDeclarations">The parsed inline style declarations.</param>
    /// <returns>The computed style.</returns>
    public static RmlComputedStyle ComputeStyle(
        RmlElement element,
        IReadOnlyList<RmlElement> ancestors,
        IReadOnlyList<RcssDocument> styleSheets,
        IReadOnlyList<RcssDeclaration> inlineDeclarations)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(ancestors);
        ArgumentNullException.ThrowIfNull(styleSheets);
        ArgumentNullException.ThrowIfNull(inlineDeclarations);

        var computed = new RmlComputedStyle();
        var winners = new Dictionary<string, (int Specificity, int Order, bool Important)>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var sheet in styleSheets)
        {
            var source = sheet.SourcePath ?? string.Empty;
            foreach (var rule in sheet.Rules)
            {
                foreach (var selector in rule.Selectors)
                {
                    if (!TryMatch(selector, element, ancestors, out var specificity))
                    {
                        continue;
                    }

                    computed.MatchedRules.Add(new RmlMatchedRule(selector, specificity, order, source));
                    ApplyDeclarations(rule.Declarations, specificity, order, computed, winners);
                }

                order++;
            }
        }

        ApplyDeclarations(inlineDeclarations, int.MaxValue, order, computed, winners);
        return computed;
    }

    /// <summary>
    /// Tries to match a selector against an element.
    /// </summary>
    /// <param name="selector">The selector text.</param>
    /// <param name="element">The element to test.</param>
    /// <param name="ancestors">The element ancestors, immediate parent first.</param>
    /// <param name="specificity">The selector specificity when matched.</param>
    /// <returns>True when the selector matches.</returns>
    public static bool TryMatch(string selector, RmlElement element, IReadOnlyList<RmlElement> ancestors, out int specificity)
    {
        specificity = 0;
        if (string.IsNullOrWhiteSpace(selector))
        {
            return false;
        }

        selector = selector.Trim();
        if (selector.IndexOf(':') >= 0)
        {
            return false;
        }

        var parts = SplitSelector(selector);
        if (parts.Count == 0)
        {
            return false;
        }

        if (!MatchesFromRight(parts, element, ancestors))
        {
            return false;
        }

        specificity = ComputeSpecificity(parts);
        return true;
    }

    private static void ApplyDeclarations(
        IReadOnlyList<RcssDeclaration> declarations,
        int specificity,
        int order,
        RmlComputedStyle computed,
        Dictionary<string, (int Specificity, int Order, bool Important)> winners)
    {
        foreach (var declaration in declarations)
        {
            var key = declaration.Property.Trim().ToLowerInvariant();
            if (key.Length == 0)
            {
                continue;
            }

            if (winners.TryGetValue(key, out var winner) && !Beats(declaration.Important, specificity, order, winner))
            {
                continue;
            }

            winners[key] = (specificity, order, declaration.Important);
            computed.Properties[key] = declaration.Value;
        }
    }

    private static bool Beats(bool important, int specificity, int order, (int Specificity, int Order, bool Important) winner)
    {
        if (important != winner.Important)
        {
            return important;
        }

        if (specificity != winner.Specificity)
        {
            return specificity > winner.Specificity;
        }

        return order >= winner.Order;
    }

    private static bool MatchesFromRight(IReadOnlyList<SelectorPart> parts, RmlElement element, IReadOnlyList<RmlElement> ancestors)
    {
        var current = element;
        var ancestorIndex = -1;
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            if (i == parts.Count - 1)
            {
                if (!MatchesCompound(parts[i].Compound, current))
                {
                    return false;
                }

                continue;
            }

            var combinator = parts[i + 1].Combinator;
            if (combinator == SelectorCombinator.Child)
            {
                ancestorIndex++;
                if (ancestorIndex >= ancestors.Count)
                {
                    return false;
                }

                current = ancestors[ancestorIndex];
                if (!MatchesCompound(parts[i].Compound, current))
                {
                    return false;
                }
            }
            else
            {
                var found = false;
                while (++ancestorIndex < ancestors.Count)
                {
                    if (MatchesCompound(parts[i].Compound, ancestors[ancestorIndex]))
                    {
                        current = ancestors[ancestorIndex];
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool MatchesCompound(string compound, RmlElement element)
    {
        var index = 0;
        var matchedType = false;
        while (index < compound.Length)
        {
            var c = compound[index];
            if (c == '*')
            {
                index++;
                continue;
            }

            if (c == '#')
            {
                var name = ReadName(compound, ref index, 1);
                if (!string.Equals(element.ElementId, name, StringComparison.Ordinal))
                {
                    return false;
                }

                continue;
            }

            if (c == '.')
            {
                var name = ReadName(compound, ref index, 1);
                if (!HasClass(element, name))
                {
                    return false;
                }

                continue;
            }

            if (c == '[')
            {
                if (!MatchesAttribute(compound, ref index, element))
                {
                    return false;
                }

                continue;
            }

            if (matchedType)
            {
                return false;
            }

            var type = ReadName(compound, ref index, 0);
            if (type.Length == 0 || !string.Equals(element.Tag, type, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            matchedType = true;
        }

        return true;
    }

    private static bool MatchesAttribute(string compound, ref int index, RmlElement element)
    {
        var end = compound.IndexOf(']', index);
        if (end < 0)
        {
            return false;
        }

        var body = compound.Substring(index + 1, end - index - 1).Trim();
        index = end + 1;
        var equals = body.IndexOf('=');
        if (equals < 0)
        {
            return body.Length > 0 && element.HasAttribute(body);
        }

        var name = body.Substring(0, equals).Trim();
        var value = body.Substring(equals + 1).Trim().Trim('"', '\'');
        return name.Length > 0 && string.Equals(element.GetAttribute(name), value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasClass(RmlElement element, string name)
    {
        foreach (var cls in element.Classes)
        {
            if (string.Equals(cls, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadName(string compound, ref int index, int skip)
    {
        index += skip;
        var start = index;
        while (index < compound.Length && IsNameChar(compound[index]))
        {
            index++;
        }

        return compound.Substring(start, index - start);
    }

    private static bool IsNameChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':';
    }

    private static int ComputeSpecificity(IReadOnlyList<SelectorPart> parts)
    {
        var ids = 0;
        var classes = 0;
        var types = 0;
        foreach (var part in parts)
        {
            CountCompound(part.Compound, ref ids, ref classes, ref types);
        }

        return (ids * IdSpecificity) + (classes * ClassSpecificity) + (types * TypeSpecificity);
    }

    private static void CountCompound(string compound, ref int ids, ref int classes, ref int types)
    {
        var index = 0;
        var sawType = false;
        while (index < compound.Length)
        {
            var c = compound[index];
            if (c == '#')
            {
                ids++;
                ReadName(compound, ref index, 1);
            }
            else if (c == '.')
            {
                classes++;
                ReadName(compound, ref index, 1);
            }
            else if (c == '[')
            {
                classes++;
                var end = compound.IndexOf(']', index);
                index = end < 0 ? compound.Length : end + 1;
            }
            else if (c == '*')
            {
                index++;
            }
            else
            {
                var type = ReadName(compound, ref index, 0);
                if (type.Length > 0 && !sawType)
                {
                    types++;
                    sawType = true;
                }
            }
        }
    }

    private static List<SelectorPart> SplitSelector(string selector)
    {
        var parts = new List<SelectorPart>();
        var current = new System.Text.StringBuilder();
        var combinator = SelectorCombinator.Descendant;
        var depth = 0;
        var index = 0;
        while (index < selector.Length)
        {
            var c = selector[index];
            if (c == '[')
            {
                depth++;
                current.Append(c);
            }
            else if (c == ']')
            {
                depth = Math.Max(0, depth - 1);
                current.Append(c);
            }
            else if (depth == 0 && (c == '>' || char.IsWhiteSpace(c)))
            {
                FlushPart(parts, current, ref combinator);
                if (c == '>')
                {
                    combinator = SelectorCombinator.Child;
                }
            }
            else
            {
                current.Append(c);
            }

            index++;
        }

        FlushPart(parts, current, ref combinator);
        return parts;
    }

    private static void FlushPart(List<SelectorPart> parts, System.Text.StringBuilder current, ref SelectorCombinator combinator)
    {
        var compound = current.ToString().Trim();
        current.Clear();
        if (compound.Length == 0)
        {
            return;
        }

        var leading = parts.Count == 0 ? SelectorCombinator.Descendant : combinator;
        parts.Add(new SelectorPart(compound, leading));
        combinator = SelectorCombinator.Descendant;
    }
}
