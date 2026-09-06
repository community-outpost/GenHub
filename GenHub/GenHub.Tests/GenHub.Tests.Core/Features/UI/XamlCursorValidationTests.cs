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
    /// Verifies that 'Default' is not a recognized <see cref="StandardCursorType"/> enum member.
    /// In Avalonia, the default pointer cursor is represented by <see cref="StandardCursorType.Arrow"/>.
    /// </summary>
    [Fact]
    public void StandardCursorType_WhenGivenDefault_FailsToParse()
    {
        var success = Enum.TryParse<StandardCursorType>("Default", ignoreCase: true, out _);
        Assert.False(success);
    }

    /// <summary>
    /// Verifies that standard cursor types like 'Arrow' and 'Hand' are valid <see cref="StandardCursorType"/> enum members.
    /// </summary>
    /// <param name="cursorType">The cursor type string to parse.</param>
    [Theory]
    [InlineData("Arrow")]
    [InlineData("Hand")]
    [InlineData("No")]
    [InlineData("Wait")]
    [InlineData("Ibeam")]
    public void StandardCursorType_WhenGivenStandardType_ParsesSuccessfully(string cursorType)
    {
        var success = Enum.TryParse<StandardCursorType>(cursorType, ignoreCase: true, out _);
        Assert.True(success);
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
    /// Determines whether the provided cursor value is recognized as a valid Avalonia cursor type.
    /// </summary>
    private static bool IsValidCursor(string cursorValue)
    {
        return Enum.TryParse<StandardCursorType>(cursorValue, ignoreCase: true, out _);
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
}
