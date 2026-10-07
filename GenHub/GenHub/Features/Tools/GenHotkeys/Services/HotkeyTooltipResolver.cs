using GenHub.Core.Constants;
using GenHub.Core.Services.Tools.GenHotkeys;
using System;

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
    /// <param name="hotkeyString">The primary CSF string label (e.g. "CONTROLBAR:ConstructAmericaDozer").</param>
    /// <param name="iconName">The icon identifier (e.g. "USADozer").</param>
    /// <param name="explicitTooltipString">An explicit tooltip label if specified in data, or null.</param>
    /// <param name="refCsf">The reference CSF file to lookup text from.</param>
    /// <returns>A tuple of (tooltipLabel, tooltipText).</returns>
    public static (string TooltipLabel, string? TooltipText) ResolveTooltip(
        string hotkeyString,
        string iconName,
        string? explicitTooltipString,
        CsfFile? refCsf)
    {
        // 1. Check explicit tooltip label first
        if (!string.IsNullOrWhiteSpace(explicitTooltipString))
        {
            var text = refCsf?.GetString(explicitTooltipString);
            return (explicitTooltipString, text);
        }

        // 2. Check known direct retail mapping table
        if (!string.IsNullOrWhiteSpace(hotkeyString) && GenHotkeysConstants.RetailActionToTooltipMap.TryGetValue(hotkeyString, out var mappedLabel))
        {
            var text = refCsf?.GetString(mappedLabel);
            return (mappedLabel, text);
        }

        // 3. Check heuristic variations against CSF
        if (refCsf != null && !string.IsNullOrWhiteSpace(hotkeyString))
        {
            var colonIdx = hotkeyString.IndexOf(':');
            var baseName = colonIdx >= 0 ? hotkeyString[(colonIdx + 1)..] : hotkeyString;

            var candidates = new[]
            {
                $"CONTROLBAR:ToolTip{baseName}",
                $"CONTROLBAR:ToolTip_{baseName}",
                $"CONTROLBAR:ToolTip{iconName}",
                $"CONTROLBAR:ToolTip_{iconName}",
                $"UPGRADE:ToolTip{baseName}",
                $"OBJECT:ToolTip{baseName}",
            };

            foreach (var candidate in candidates)
            {
                var text = refCsf.GetString(candidate);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return (candidate, text);
                }
            }
        }

        // 4. Default synthetic label if not present in retail CSF
        var defaultLabel = !string.IsNullOrWhiteSpace(hotkeyString)
            ? (hotkeyString.StartsWith("CONTROLBAR:", StringComparison.OrdinalIgnoreCase)
                ? $"CONTROLBAR:ToolTip{hotkeyString["CONTROLBAR:".Length..]}"
                : $"CONTROLBAR:ToolTip{hotkeyString}")
            : $"CONTROLBAR:ToolTip{iconName}";

        return (defaultLabel, null);
    }
}
