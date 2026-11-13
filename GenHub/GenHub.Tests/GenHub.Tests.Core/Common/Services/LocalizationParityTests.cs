using GenHub.Tests.Core.Features.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Unit tests verifying localization resource file integrity, 1:1 key parity across all cultures,
/// XAML markup key validity, and absence of hardcoded user-facing literals.
/// </summary>
public partial class LocalizationParityTests
{
    private static readonly string LocalizationDirectory = Path.Combine(
        UiTestPathHelper.FindSolutionDirectory(),
        "GenHub",
        "Resources",
        "Localization");

    private static readonly string GenHubProjectDirectory = Path.Combine(
        UiTestPathHelper.FindSolutionDirectory(),
        "GenHub");

    /// <summary>
    /// Verifies that all supported locale resource files maintain exact 1:1 key parity with the default locale.
    /// Fails CI if any locale is missing keys or contains obsolete/extra keys.
    /// </summary>
    [Fact]
    public void Localizations_ShouldMatchDefaultKeys()
    {
        var sourceKeys = LoadKeys("default");

        foreach (var locale in AllSupportedLocales())
        {
            var localizedKeys = LoadKeys(locale);

            var missingKeys = sourceKeys.Except(localizedKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var extraKeys = localizedKeys.Except(sourceKeys).OrderBy(k => k, StringComparer.Ordinal).ToList();

            var message = $"""
            Localization parity failed for: {locale}

            Missing keys:
            {string.Join("\n", missingKeys)}

            Extra keys:
            {string.Join("\n", extraKeys)}
            """;

            Assert.True(!missingKeys.Any() && !extraKeys.Any(), message);
        }
    }

    /// <summary>
    /// Verifies that no localization resource entry contains empty or whitespace-only values.
    /// </summary>
    [Fact]
    public void Localizations_ShouldNotHaveEmptyOrWhitespaceValues()
    {
        var locales = new List<string> { "default" };
        locales.AddRange(AllSupportedLocales());

        var emptyEntries = new List<string>();

        foreach (var locale in locales)
        {
            var path = GetResxFilePath(locale);
            var entries = LoadEntries(path);

            foreach (var (key, value) in entries)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    emptyEntries.Add($"[{locale}] {key} has empty or whitespace value");
                }
            }
        }

        Assert.True(
            emptyEntries.Count == 0,
            $"Found empty or whitespace localization entries:\n{string.Join("\n", emptyEntries)}");
    }

    /// <summary>
    /// Verifies that composite format placeholders (e.g. {{0}}, {{1}}) in the default culture match
    /// exactly across all localized cultures to prevent runtime format exceptions.
    /// </summary>
    [Fact]
    public void Localizations_ShouldMatchFormatPlaceholdersAcrossLocales()
    {
        var defaultEntries = LoadEntries(GetResxFilePath("default"));
        var placeholderRegex = PlaceholderRegex();

        var mismatches = new List<string>();

        foreach (var locale in AllSupportedLocales())
        {
            var localizedEntries = LoadEntries(GetResxFilePath(locale));

            foreach (var (key, defaultValue) in defaultEntries)
            {
                var defaultPlaceholders = placeholderRegex.Matches(defaultValue)
                    .Select(m => m.Value)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList();

                if (defaultPlaceholders.Count == 0)
                {
                    continue;
                }

                if (!localizedEntries.TryGetValue(key, out var localizedValue))
                {
                    continue; // Covered by parity test
                }

                var localizedPlaceholders = placeholderRegex.Matches(localizedValue)
                    .Select(m => m.Value)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList();

                if (!defaultPlaceholders.SequenceEqual(localizedPlaceholders))
                {
                    mismatches.Add(
                        $"[{locale}] {key}: expected [{string.Join(", ", defaultPlaceholders)}], found [{string.Join(", ", localizedPlaceholders)}]");
                }
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"Format placeholder mismatches detected:\n{string.Join("\n", mismatches)}");
    }

    /// <summary>
    /// Scans all XAML (.axaml) files in the application and verifies that all keys referenced by
    /// Localize markup extensions exist in the default Strings.resx resource file.
    /// </summary>
    [Fact]
    public void Xaml_LocalizeMarkupExtensions_ShouldReferenceExistingKeys()
    {
        var defaultKeys = LoadKeys("default");
        var localizePattern = LocalizeMarkupRegex();
        var invalidReferences = new List<string>();

        var axamlFiles = Directory.GetFiles(GenHubProjectDirectory, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (var file in axamlFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var matches = localizePattern.Matches(lines[i]);
                foreach (Match match in matches)
                {
                    var key = match.Groups[1].Value.Trim();
                    if (!defaultKeys.Contains(key))
                    {
                        var relativePath = Path.GetRelativePath(GenHubProjectDirectory, file);
                        invalidReferences.Add($"{relativePath}:{i + 1} -> Key '{key}' not found in Strings.resx");
                    }
                }
            }
        }

        Assert.True(
            invalidReferences.Count == 0,
            $"Found invalid localization keys referenced in XAML:\n{string.Join("\n", invalidReferences)}");
    }

    /// <summary>
    /// Scans all application XAML (.axaml) files to ensure that user-facing attributes (Text, Content, Header, ToolTip.Tip, Watermark)
    /// do not contain hardcoded English string literals, enforcing the use of localization markup extensions.
    /// </summary>
    [Fact]
    public void Xaml_UserFacingElements_ShouldNotHaveHardcodedTextLiterals()
    {
        var userFacingAttrPattern = UserFacingAttributeRegex();
        var unlocalizedFindings = new List<string>();

        var axamlFiles = Directory.GetFiles(GenHubProjectDirectory, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !Path.GetFileName(f).Equals("App.axaml", StringComparison.OrdinalIgnoreCase));

        foreach (var file in axamlFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var matches = userFacingAttrPattern.Matches(lines[i]);
                foreach (Match match in matches)
                {
                    var val = match.Groups[2].Value.Trim();
                    if (IsExemptLiteral(val))
                    {
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(GenHubProjectDirectory, file);
                    unlocalizedFindings.Add($"{relativePath}:{i + 1} -> {match.Value}");
                }
            }
        }

        Assert.True(
            unlocalizedFindings.Count == 0,
            $"Found hardcoded user-facing strings in XAML. Please use {{localization:Localize ...}}:\n{string.Join("\n", unlocalizedFindings)}");
    }

    private static bool IsExemptLiteral(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        // Avalonia bindings, resources, or markup extensions
        if (value.StartsWith('{') && value.EndsWith('}'))
        {
            return true;
        }

        // Pure numbers, symbols, icons, emojis, or punctuation without word letters
        if (!value.Any(char.IsLetter))
        {
            return true;
        }

        // Single keycaps or function keys (e.g. "A", "W", "F1"-"F12")
        if (Regex.IsMatch(value, @"^[A-Z0-9]$|^F[0-9]{1,2}$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        // Trademarks, brand names, technical protocols, and file extensions
        if (Regex.IsMatch(
            value,
            @"^(GenHub|ModDB|GitHub|CNCNet|GameSpy|Windows|DirectX|QuickStart|SAGE|W3D|Discord|Reddit|YouTube|Twitch|CRC|URL|ID|C&C|Zero Hour)$",
            RegexOptions.IgnoreCase))
        {
            return true;
        }

        // Known command-line argument examples, token prefixes, or technical formats
        if (Regex.IsMatch(
            value,
            @"^(-win|-quickstart|ghp_.*|http.*|https.*|\.png|\.jpg|\.ico|\.svg|\.json|.*-win -quickstart.*)$",
            RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static HashSet<string> LoadKeys(string locale)
    {
        var path = GetResxFilePath(locale);
        var doc = XDocument.Load(path);
        return doc.Root?
            .Elements("data")
            .Select(e => e.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal) ?? [];
    }

    private static Dictionary<string, string> LoadEntries(string path)
    {
        var doc = XDocument.Load(path);
        return doc.Root?
            .Elements("data")
            .Where(e => !string.IsNullOrEmpty(e.Attribute("name")?.Value))
            .ToDictionary(
                e => e.Attribute("name")!.Value,
                e => e.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal) ?? [];
    }

    private static IEnumerable<string> AllSupportedLocales()
    {
        if (!Directory.Exists(LocalizationDirectory))
        {
            yield break;
        }

        var files = Directory.GetFiles(LocalizationDirectory, "Strings.*.resx");
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var parts = fileName.Split('.');
            if (parts.Length == 3 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                yield return parts[1];
            }
        }
    }

    private static string GetResxFilePath(string locale)
    {
        return locale == "default"
            ? Path.Combine(LocalizationDirectory, "Strings.resx")
            : Path.Combine(LocalizationDirectory, $"Strings.{locale}.resx");
    }

    [GeneratedRegex(@"\{[0-9]+\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"\{\s*(?:localization:)?Localize\s+([A-Za-z0-9_\.]+)")]
    private static partial Regex LocalizeMarkupRegex();

    [GeneratedRegex(@"\b(Text|Content|Header|ToolTip\.Tip|Watermark)\s*=\s*""([^""]+)""")]
    private static partial Regex UserFacingAttributeRegex();
}
