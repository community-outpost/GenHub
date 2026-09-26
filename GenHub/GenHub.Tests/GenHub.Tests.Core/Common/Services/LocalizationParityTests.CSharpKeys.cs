using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Verifies that every resource key referenced by <c>GetString("...")</c> calls in
/// production code exists in the default <c>Strings.resx</c> resource file.
/// The localization service returns the key itself when a resource is missing,
/// so a missing key surfaces to users as raw text instead of failing fast.
/// </summary>
public partial class LocalizationParityTests
{
    /// <summary>
    /// Scans all production C# files for <c>GetString</c> resource keys and verifies
    /// that each key exists in the default Strings.resx resource file.
    /// </summary>
    [Fact]
    public void CSharp_GetStringCalls_ShouldReferenceExistingKeys()
    {
        var defaultKeys = LoadKeys("default");
        var keyPattern = CSharpGetStringRegex();
        var invalidReferences = new List<string>();

        var csFiles = Directory.GetFiles(GenHubProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        foreach (var file in csFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var matches = keyPattern.Matches(lines[i]);
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
            $"Found invalid localization keys referenced in C#:\n{string.Join("\n", invalidReferences)}");
    }

    [GeneratedRegex("GetString\\(\\s*\"([A-Za-z0-9_\\.]+)\"")]
    private static partial Regex CSharpGetStringRegex();
}
