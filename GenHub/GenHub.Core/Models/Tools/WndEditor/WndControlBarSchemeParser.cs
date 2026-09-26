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
        ["ButtonOptions"] = WndConstants.ControlBarScheme.ButtonOptionsKey,
        ["IdleWorkerButtonEnable"] = WndConstants.ControlBarScheme.ButtonIdleWorkerKey,
        ["ButtonIdleWorker"] = WndConstants.ControlBarScheme.ButtonIdleWorkerKey,
        ["BuddyButtonEnable"] = WndConstants.ControlBarScheme.ButtonChatKey,
        ["ButtonChat"] = WndConstants.ControlBarScheme.ButtonChatKey,
        ["BeaconButtonEnable"] = WndConstants.ControlBarScheme.ButtonPlaceBeaconKey,
        ["ButtonPlaceBeacon"] = WndConstants.ControlBarScheme.ButtonPlaceBeaconKey,
        ["GeneralButtonEnable"] = WndConstants.ControlBarScheme.ButtonGeneralKey,
        ["ButtonGeneral"] = WndConstants.ControlBarScheme.ButtonGeneralKey,
        ["UAttackButtonEnable"] = WndConstants.ControlBarScheme.ButtonUAttackKey,
        ["ButtonUAttack"] = WndConstants.ControlBarScheme.ButtonUAttackKey,
        ["ExpBarForegroundImage"] = WndConstants.ControlBarScheme.ExpBarForegroundKey,
        ["ExpBarForeground"] = WndConstants.ControlBarScheme.ExpBarForegroundKey,
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

            var tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            var firstToken = tokens[0];

            if (IsSchemeBlockHeader(tokens, firstToken))
            {
                var schemeName = ParseSchemeName(tokens);
                currentSchemeDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                schemes.Add((schemeName, currentSchemeDict));
                inImagePart = false;
                continue;
            }

            if (string.Equals(firstToken, WndConstants.ControlBarScheme.EndKeyword, StringComparison.OrdinalIgnoreCase))
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
            ProcessLine(line, tokens, target, ref inImagePart);
        }
    }

    private static bool IsSchemeBlockHeader(string[] tokens, string firstToken)
    {
        return string.Equals(firstToken, WndConstants.ControlBarScheme.SchemeKeyword, StringComparison.OrdinalIgnoreCase) ||
            (tokens.Length == 1 && firstToken.StartsWith(WndConstants.ControlBarScheme.SchemeKeyword, StringComparison.OrdinalIgnoreCase));
    }

    private static void ProcessLine(string line, string[] tokens, Dictionary<string, string> target, ref bool inImagePart)
    {
        var firstToken = tokens[0];

        if (string.Equals(firstToken, WndConstants.ControlBarScheme.ImagePartKeyword, StringComparison.OrdinalIgnoreCase))
        {
            ProcessImagePartLine(line, target, ref inImagePart);
            return;
        }

        if (inImagePart)
        {
            ProcessImagePartBlockLine(line, target);
            return;
        }

        ParseKeyValueLine(line, tokens, target);
    }

    private static void ProcessImagePartLine(string line, Dictionary<string, string> target, ref bool inImagePart)
    {
        var imageNameIndex = line.IndexOf(WndConstants.ControlBarScheme.ImageNameKeyword, StringComparison.OrdinalIgnoreCase);
        if (imageNameIndex >= 0)
        {
            // Inline ImagePart: extract image value without leaving an existing outer ImagePart block
            var imageValue = ExtractValueAfterKey(line[imageNameIndex..], WndConstants.ControlBarScheme.ImageNameKeyword);
            if (!string.IsNullOrEmpty(imageValue))
            {
                target[WndConstants.ControlBarScheme.BackgroundMarkerKey] = imageValue;
            }
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

    private static void ParseKeyValueLine(string line, string[] tokens, Dictionary<string, string> target)
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

        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            value = value[1..^1];
        }

        if (KeyMap.TryGetValue(key, out var mappedKey))
        {
            target[mappedKey] = value;
        }
    }

    private static string StripComment(string line)
    {
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (inQuotes)
            {
                continue;
            }

            if (c == ';')
            {
                return line[..i].Trim();
            }

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                // Preserve URL protocol markers (e.g. http://)
                if (i > 0 && line[i - 1] == ':')
                {
                    continue;
                }

                return line[..i].Trim();
            }
        }

        return line.Trim();
    }

    private static string ParseSchemeName(string[] tokens)
    {
        if (tokens.Length > 2 && (tokens[1] == "=" || tokens[1] == ":"))
        {
            return tokens[2];
        }

        if (tokens.Length > 1)
        {
            return tokens[1];
        }

        var single = tokens[0];
        if (single.StartsWith(WndConstants.ControlBarScheme.SchemeKeyword, StringComparison.OrdinalIgnoreCase))
        {
            var stripped = single[WndConstants.ControlBarScheme.SchemeKeyword.Length..].TrimStart('=', ':', ' ', '\t');
            if (!string.IsNullOrEmpty(stripped))
            {
                return stripped;
            }
        }

        return single;
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
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        var token = tokens[0];
        if (token.Length >= 2 && token.StartsWith('"') && token.EndsWith('"'))
        {
            token = token[1..^1];
        }

        return token;
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
            if (!string.IsNullOrEmpty(preferredScheme))
            {
                var preferred = schemes.Find(s => s.Name.Equals(preferredScheme, StringComparison.OrdinalIgnoreCase));
                selectedScheme = preferred.Overrides;
            }

            if (selectedScheme == null)
            {
                var america = schemes.Find(s => s.Name.Contains(WndConstants.ControlBarScheme.AmericaFaction, StringComparison.OrdinalIgnoreCase));
                selectedScheme = america.Overrides ?? schemes[0].Overrides;
            }
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
