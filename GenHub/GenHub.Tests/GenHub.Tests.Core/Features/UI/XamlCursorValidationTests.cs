using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Input;
using Xunit;

namespace GenHub.Tests.Core.Features.UI;

/// <summary>
/// Regression and static validation tests ensuring all AXAML templates define valid Avalonia cursor types.
/// </summary>
public partial class XamlCursorValidationTests
{
    /// <summary>
    /// Matches AXAML attributes of the form Cursor="value".
    /// </summary>
    [GeneratedRegex(@"Cursor\s*=\s*""([^""{}]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex CursorAttributeRegex();

    /// <summary>
    /// Matches AXAML style setters of the form &lt;Setter Property="Cursor" Value="value" /&gt;.
    /// </summary>
    [GeneratedRegex(@"<Setter[^>]+Property\s*=\s*""Cursor""[^>]+Value\s*=\s*""([^""{}]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex CursorSetterRegex();

    /// <summary>
    /// Matches AXAML style setters of the form &lt;Setter Value="value" Property="Cursor" /&gt;.
    /// </summary>
    [GeneratedRegex(@"<Setter[^>]+Value\s*=\s*""([^""{}]+)""[^>]+Property\s*=\s*""Cursor""", RegexOptions.IgnoreCase)]
    private static partial Regex CursorSetterReversedRegex();

    /// <summary>
    /// Verifies that Avalonia throws an <see cref="ArgumentException"/> when parsing the unrecognized cursor type 'Default'.
    /// </summary>
    [Fact]
    public void CursorParse_WhenGivenDefault_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Cursor.Parse("Default"));
    }

    /// <summary>
    /// Verifies that Avalonia successfully parses standard cursor types like 'Arrow' and 'Hand'.
    /// </summary>
    /// <param name="cursorType">The cursor type string to parse.</param>
    [Theory]
    [InlineData("Arrow")]
    [InlineData("Hand")]
    [InlineData("No")]
    [InlineData("Wait")]
    [InlineData("Ibeam")]
    public void CursorParse_WhenGivenStandardType_ParsesSuccessfully(string cursorType)
    {
        var cursor = Cursor.Parse(cursorType);
        Assert.NotNull(cursor);
    }

    /// <summary>
    /// Scans every AXAML file in the solution and asserts that all cursor attributes and setters
    /// specify valid Avalonia <see cref="StandardCursorType"/> values.
    /// </summary>
    [Fact]
    public void AllAxamlFiles_ContainValidCursorValues()
    {
        var repoRoot = FindRepositoryRoot();
        var genHubDirectory = Path.Combine(repoRoot, "GenHub");
        Assert.True(Directory.Exists(genHubDirectory), $"GenHub directory not found at: {genHubDirectory}");

        var axamlFiles = Directory.GetFiles(genHubDirectory, "*.axaml", SearchOption.AllDirectories);
        Assert.NotEmpty(axamlFiles);

        var errors = new List<string>();

        foreach (var file in axamlFiles)
        {
            var content = File.ReadAllText(file);

            // Check Cursor="..." attributes
            foreach (Match match in CursorAttributeRegex().Matches(content))
            {
                var cursorValue = match.Groups[1].Value.Trim();
                if (!IsValidCursor(cursorValue))
                {
                    errors.Add($"{Path.GetRelativePath(repoRoot, file)}: Invalid Cursor attribute '{cursorValue}'");
                }
            }

            // Check <Setter Property="Cursor" Value="..." />
            foreach (Match match in CursorSetterRegex().Matches(content))
            {
                var cursorValue = match.Groups[1].Value.Trim();
                if (!IsValidCursor(cursorValue))
                {
                    errors.Add($"{Path.GetRelativePath(repoRoot, file)}: Invalid Cursor setter value '{cursorValue}'");
                }
            }

            // Check <Setter Value="..." Property="Cursor" />
            foreach (Match match in CursorSetterReversedRegex().Matches(content))
            {
                var cursorValue = match.Groups[1].Value.Trim();
                if (!IsValidCursor(cursorValue))
                {
                    errors.Add($"{Path.GetRelativePath(repoRoot, file)}: Invalid Cursor setter value '{cursorValue}'");
                }
            }
        }

        Assert.True(errors.Count == 0, $"Found invalid cursor values in AXAML files:\n{string.Join(Environment.NewLine, errors)}");
    }

    /// <summary>
    /// Determines whether the provided cursor value is recognized by Avalonia.
    /// </summary>
    private static bool IsValidCursor(string cursorValue)
    {
        try
        {
            _ = Cursor.Parse(cursorValue);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Finds the repository root by searching upward from the current execution base directory.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GenHub.sln")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GenHub.sln")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing GenHub.sln or .git from: " + AppContext.BaseDirectory);
    }
}
