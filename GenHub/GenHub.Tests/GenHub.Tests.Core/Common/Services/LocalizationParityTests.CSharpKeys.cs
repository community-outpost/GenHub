using GenHub.Tests.Core.Features.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Verifies that every resource key referenced by localization lookup calls in
/// production code exists in the default <c>Strings.resx</c> resource file.
/// The localization service returns the key itself when a resource is missing,
/// so a missing key surfaces to users as raw text instead of failing fast.
/// </summary>
public partial class LocalizationParityTests
{
    /// <summary>
    /// Scans every production C# file in the solution for localization
    /// <c>GetString</c> and <c>GetLocalizedString</c> resource keys and verifies
    /// that each key exists in the default Strings.resx resource file. The scan
    /// matches full file contents so keys split across line breaks are detected,
    /// only counts <c>GetString</c> receiver chains that resolve through the
    /// localization service (plus bare helper calls), counts every
    /// <c>GetLocalizedString</c> call regardless of receiver, and excludes test
    /// projects and build output directories.
    /// </summary>
    [Fact]
    public void CSharp_LocalizationCalls_ShouldReferenceExistingKeys()
    {
        var defaultKeys = LoadKeys("default");
        var keyPattern = CSharpGetStringRegex();
        var invalidReferences = new List<string>();
        var solutionDirectory = UiTestPathHelper.FindSolutionDirectory();

        var csFiles = Directory.GetFiles(solutionDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(IsProductionSourceFile);

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match match in keyPattern.Matches(content))
            {
                var method = match.Groups["method"].Value;
                var receiver = match.Groups["receiver"].Value;
                if (!IsLocalizationCall(method, receiver))
                {
                    continue;
                }

                var key = match.Groups["key"].Value.Trim();
                if (!defaultKeys.Contains(key))
                {
                    var relativePath = Path.GetRelativePath(solutionDirectory, file);
                    var lineNumber = CountLines(content, match.Index) + 1;
                    invalidReferences.Add($"{relativePath}:{lineNumber} -> Key '{key}' not found in Strings.resx");
                }
            }
        }

        Assert.True(
            invalidReferences.Count == 0,
            $"Found invalid localization keys referenced in C#:\n{string.Join("\n", invalidReferences)}");
    }

    private static bool IsProductionSourceFile(string file)
    {
        var segments = file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Any(s =>
            s.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Tests", StringComparison.Ordinal));
    }

    private static bool IsLocalizationCall(string method, string receiver)
    {
        if (method.Equals("GetLocalizedString", StringComparison.Ordinal))
        {
            return true;
        }

        return receiver.Length == 0 || IsLocalizationReceiver(receiver);
    }

    private static bool IsLocalizationReceiver(string receiver)
    {
        var segments = receiver.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Any(segment =>
            segment.TrimEnd('?').Contains("localiz", StringComparison.OrdinalIgnoreCase) ||
            segment.TrimEnd('?').Equals("loc", StringComparison.OrdinalIgnoreCase));
    }

    private static int CountLines(string content, int index)
    {
        var count = 0;
        for (var i = 0; i < index; i++)
        {
            if (content[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    [GeneratedRegex("(?<![\\w.])(?<receiver>(?:[A-Za-z_][A-Za-z0-9_]*\\??\\.\\s*)*)(?<method>GetLocalizedString|GetString)\\(\\s*\"(?<key>[A-Za-z0-9_\\.]+)\"")]
    private static partial Regex CSharpGetStringRegex();
}
