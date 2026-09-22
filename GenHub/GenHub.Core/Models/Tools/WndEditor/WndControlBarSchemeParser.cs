using GenHub.Core.Constants;
using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Parser for Command &amp; Conquer Generals and Zero Hour ControlBarScheme.ini files.
/// Supports retail multi-line scheme blocks and flat key-value overrides.
/// </summary>
public static class WndControlBarSchemeParser
{
    private static readonly IReadOnlyDictionary<string, string> KeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RightHUDImage"] = WndConstants.ControlBarScheme.RightHUDKey,
        ["RightHUD"] = WndConstants.ControlBarScheme.RightHUDKey,
        ["OptionsButtonEnable"] = WndConstants.ControlBarScheme.ButtonOptionsKey,
        ["IdleWorkerButtonEnable"] = WndConstants.ControlBarScheme.ButtonIdleWorkerKey,
        ["BuddyButtonEnable"] = WndConstants.ControlBarScheme.ButtonChatKey,
        ["BeaconButtonEnable"] = WndConstants.ControlBarScheme.ButtonPlaceBeaconKey,
        ["GeneralButtonEnable"] = WndConstants.ControlBarScheme.ButtonGeneralKey,
        ["UAttackButtonEnable"] = WndConstants.ControlBarScheme.ButtonUAttackKey,
        ["ExpBarForegroundImage"] = WndConstants.ControlBarScheme.ExpBarForegroundKey,
        ["QueueButtonImage"] = WndConstants.ControlBarScheme.QueueButtonImageKey,
    };

    /// <summary>
    /// Parses ControlBarScheme.ini content and populates the given result dictionary with resolved override values.
    /// </summary>
    /// <param name="iniText">Raw text of ControlBarScheme.ini.</param>
    /// <param name="result">Dictionary to populate with resolved overrides.</param>
    /// <param name="preferredScheme">Preferred scheme name, defaults to ControlBarSchemeAmerica.</param>
    public static void Parse(string iniText, Dictionary<string, string> result, string? preferredScheme = WndConstants.ControlBarScheme.AmericaSchemeName)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(iniText))
        {
            return;
        }

        var schemes = new List<(string Name, Dictionary<string, string> Overrides)>();
        var flatOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ParseLines(iniText, schemes, flatOverrides);
        SelectSchemeOverrides(schemes, flatOverrides, preferredScheme, result);
    }

    private static void ParseLines(
        string iniText,
        List<(string Name, Dictionary<string, string> Overrides)> schemes,
        Dictionary<string, string> flatOverrides)
    {
        Dictionary<string, string>? currentSchemeDict = null;
        var inImagePart = false;

        var lines = iniText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = StripComment(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith(WndConstants.ControlBarScheme.SchemeKeyword, StringComparison.OrdinalIgnoreCase))
            {
                var schemeName = ParseSchemeName(line);
                currentSchemeDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                schemes.Add((schemeName, currentSchemeDict));
                inImagePart = false;
                continue;
            }

            if (string.Equals(line, WndConstants.ControlBarScheme.EndKeyword, StringComparison.OrdinalIgnoreCase))
            {
                if (inImagePart)
                {
                    inImagePart = false;
                }
                else
                {
                    currentSchemeDict = null;
                }

                continue;
            }

            var target = currentSchemeDict ?? flatOverrides;
            ProcessLine(line, target, ref inImagePart);
        }
    }

    private static void ProcessLine(string line, Dictionary<string, string> target, ref bool inImagePart)
    {
        if (line.StartsWith(WndConstants.ControlBarScheme.ImagePartKeyword, StringComparison.OrdinalIgnoreCase))
        {
            ProcessImagePartLine(line, target, ref inImagePart);
            return;
        }

        if (inImagePart)
        {
            ProcessImagePartBlockLine(line, target);
            return;
        }

        ParseKeyValueLine(line, target);
    }

    private static void ProcessImagePartLine(string line, Dictionary<string, string> target, ref bool inImagePart)
    {
        var imageNameIndex = line.IndexOf(WndConstants.ControlBarScheme.ImageNameKeyword, StringComparison.OrdinalIgnoreCase);
        if (imageNameIndex >= 0)
        {
            // Inline ImagePart: extract image value and do NOT enter block mode
            var imageValue = ExtractValueAfterKey(line[imageNameIndex..], WndConstants.ControlBarScheme.ImageNameKeyword);
            if (!string.IsNullOrEmpty(imageValue))
            {
                target[WndConstants.ControlBarScheme.BackgroundMarkerKey] = imageValue;
            }

            inImagePart = false;
        }
        else
        {
            inImagePart = true;
        }
    }

    private static void ProcessImagePartBlockLine(string line, Dictionary<string, string> target)
    {
        if (line.IndexOf(WndConstants.ControlBarScheme.ImageNameKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var imageValue = ExtractValueAfterKey(line, WndConstants.ControlBarScheme.ImageNameKeyword);
            if (!string.IsNullOrEmpty(imageValue))
            {
                target[WndConstants.ControlBarScheme.BackgroundMarkerKey] = imageValue;
            }
        }
    }

    private static void ParseKeyValueLine(string line, Dictionary<string, string> target)
    {
        string key;
        string value;

        var separatorIndex = line.IndexOfAny(['=', ':']);
        if (separatorIndex > 0)
        {
            key = line[..separatorIndex].Trim();
            value = line[(separatorIndex + 1)..].Trim();
        }
        else
        {
            var tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
            {
                return;
            }

            key = tokens[0];
            value = tokens[1];
        }

        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
        {
            return;
        }

        var mappedKey = KeyMap.TryGetValue(key, out var mk) ? mk : key;
        target[mappedKey] = value;
    }

    private static string StripComment(string line)
    {
        var commentIndex = line.IndexOf(';');
        if (commentIndex >= 0)
        {
            line = line[..commentIndex];
        }

        var slashIndex = line.IndexOf("//", StringComparison.Ordinal);
        if (slashIndex >= 0)
        {
            line = line[..slashIndex];
        }

        return line.Trim();
    }

    private static string ParseSchemeName(string line)
    {
        var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : string.Empty;
    }

    private static string ExtractValueAfterKey(string segment, string key)
    {
        var keyIdx = segment.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (keyIdx < 0)
        {
            return string.Empty;
        }

        var afterKey = segment[(keyIdx + key.Length)..].TrimStart(':', '=', ' ', '\t').Trim();
        var tokens = afterKey.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 ? tokens[0] : string.Empty;
    }

    private static void SelectSchemeOverrides(
        List<(string Name, Dictionary<string, string> Overrides)> schemes,
        Dictionary<string, string> flatOverrides,
        string? preferredScheme,
        Dictionary<string, string> result)
    {
        Dictionary<string, string>? selectedScheme = null;

        if (schemes.Count > 0)
        {
            selectedScheme = schemes.Find(s =>
                (!string.IsNullOrEmpty(preferredScheme) && s.Name.Equals(preferredScheme, StringComparison.OrdinalIgnoreCase)) ||
                s.Name.Contains(WndConstants.ControlBarScheme.AmericaFaction, StringComparison.OrdinalIgnoreCase)).Overrides;

            selectedScheme ??= schemes[0].Overrides;
        }

        if (selectedScheme != null)
        {
            foreach (var (k, v) in selectedScheme)
            {
                result[k] = v;
            }
        }

        foreach (var (k, v) in flatOverrides)
        {
            result[k] = v;
        }
    }
}
