using GenHub.Core.Constants;
using GenHub.Core.Services.Tools.GenHotkeys;
using System;
using System.Collections.Generic;

namespace GenHub.Features.Tools.GenHotkeys.Services;

/// <summary>
/// Resolves SAGE in-game tooltip description labels and text from retail CSF strings and CommandButton definitions.
/// Correlates command buttons and actions with their in-game DescriptLabel keys.
/// </summary>
public static class HotkeyTooltipResolver
{
    /// <summary>
    /// Resolves the in-game tooltip description label and text for an action.
    /// </summary>
    /// <param name="hotkeyString">The primary CSF string label (e.g. &quot;CONTROLBAR:ConstructAmericaDozer&quot;).</param>
    /// <param name="iconName">The icon identifier (e.g. &quot;USADozer&quot;).</param>
    /// <param name="explicitTooltipString">An explicit tooltip label if specified in data, or null.</param>
    /// <param name="refCsf">The reference CSF file to lookup text from.</param>
    /// <returns>A tuple of (tooltipLabel, tooltipText).</returns>
    public static (string TooltipLabel, string? TooltipText) ResolveTooltip(
        string hotkeyString,
        string iconName,
        string? explicitTooltipString,
        CsfFile? refCsf)
    {
        if (TryResolveExplicitOrKnown(explicitTooltipString, hotkeyString, refCsf, out var directResult))
        {
            return directResult;
        }

        if (refCsf != null && TryResolveHeuristics(hotkeyString, iconName, refCsf, out var heuristicResult))
        {
            return heuristicResult;
        }

        return (GenerateDefaultSyntheticLabel(hotkeyString, iconName), null);
    }

    private static bool TryResolveExplicitOrKnown(
        string? explicitTooltipString,
        string hotkeyString,
        CsfFile? refCsf,
        out (string TooltipLabel, string? TooltipText) result)
    {
        if (!string.IsNullOrWhiteSpace(explicitTooltipString))
        {
            result = (explicitTooltipString, refCsf?.GetString(explicitTooltipString));
            return true;
        }

        if (!string.IsNullOrWhiteSpace(hotkeyString) &&
            GenHotkeysConstants.RetailActionToTooltipMap.TryGetValue(hotkeyString, out var mappedLabel))
        {
            result = (mappedLabel, refCsf?.GetString(mappedLabel));
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryResolveHeuristics(
        string hotkeyString,
        string iconName,
        CsfFile refCsf,
        out (string TooltipLabel, string? TooltipText) result)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString) && string.IsNullOrWhiteSpace(iconName))
        {
            result = default;
            return false;
        }

        var candidates = BuildHeuristicCandidates(hotkeyString, iconName);
        foreach (var candidate in candidates)
        {
            var text = refCsf.GetString(candidate);
            if (!string.IsNullOrWhiteSpace(text))
            {
                result = (candidate, text);
                return true;
            }
        }

        result = default;
        return false;
    }

    private static List<string> BuildHeuristicCandidates(string hotkeyString, string iconName)
    {
        var candidates = new List<string>(8);
        if (!string.IsNullOrWhiteSpace(hotkeyString))
        {
            AddHotkeyCandidates(candidates, hotkeyString);
        }

        if (!string.IsNullOrWhiteSpace(iconName))
        {
            candidates.Add($"CONTROLBAR:ToolTip{iconName}");
            candidates.Add($"CONTROLBAR:ToolTip_{iconName}");
        }

        return candidates;
    }

    private static void AddHotkeyCandidates(List<string> candidates, string hotkeyString)
    {
        var colonIdx = hotkeyString.IndexOf(':');
        var prefix = colonIdx >= 0 ? hotkeyString[..colonIdx] : "CONTROLBAR";
        var baseName = colonIdx >= 0 ? hotkeyString[(colonIdx + 1)..] : hotkeyString;

        candidates.Add($"{prefix}:ToolTip{baseName}");
        candidates.Add($"{prefix}:ToolTip_{baseName}");

        if (!prefix.Equals("CONTROLBAR", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add($"CONTROLBAR:ToolTip{baseName}");
            candidates.Add($"CONTROLBAR:ToolTip_{baseName}");
        }

        if (!prefix.Equals("UPGRADE", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add($"UPGRADE:ToolTip{baseName}");
        }

        if (!prefix.Equals("OBJECT", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add($"OBJECT:ToolTip{baseName}");
        }
    }

    private static string GenerateDefaultSyntheticLabel(string hotkeyString, string iconName)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
        {
            return $"CONTROLBAR:ToolTip{iconName}";
        }

        var colonIndex = hotkeyString.IndexOf(':');
        if (colonIndex < 0)
        {
            return $"CONTROLBAR:ToolTip{hotkeyString}";
        }

        var prefix = hotkeyString[..colonIndex];
        var suffix = hotkeyString[(colonIndex + 1)..];
        return suffix.StartsWith("ToolTip", StringComparison.OrdinalIgnoreCase)
            ? hotkeyString
            : $"{prefix}:ToolTip{suffix}";
    }
}
