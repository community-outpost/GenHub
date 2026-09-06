using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Input;
using Xunit;

namespace GenHub.Tests.Core.Features.UI;

/// <summary>
/// Regression and static validation tests ensuring all AXAML templates across the solution define valid Avalonia cursor types.
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
    [InlineData("SizeWestEast")]
    [InlineData("SizeNorthSouth")]
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
        var solutionDir = UiTestPathHelper.FindSolutionDirectory();
        Assert.True(Directory.Exists(solutionDir), $"Solution directory not found at: {solutionDir}");

        var axamlFiles = Directory.GetFiles(solutionDir, "*.axaml", SearchOption.AllDirectories);
        Assert.NotEmpty(axamlFiles);

        var errors = new List<string>();

        foreach (var file in axamlFiles)
        {
            var rawContent = File.ReadAllText(file);

            // Strip XML comments before analyzing to avoid false positives inside commented blocks
            var content = XmlCommentRegex().Replace(rawContent, string.Empty);

            // Check Cursor="..." attributes
            foreach (Match match in CursorAttributeRegex().Matches(content))
            {
                var cursorValue = match.Groups[1].Value.Trim();
                if (!IsValidCursor(cursorValue))
                {
                    errors.Add($"{Path.GetRelativePath(solutionDir, file)}: Invalid Cursor attribute '{cursorValue}'");
                }
            }

            // Check <Setter Property="Cursor" Value="..." />
            foreach (Match match in CursorSetterRegex().Matches(content))
            {
                var cursorValue = match.Groups[1].Value.Trim();
                if (!IsValidCursor(cursorValue))
                {
                    errors.Add($"{Path.GetRelativePath(solutionDir, file)}: Invalid Cursor setter value '{cursorValue}'");
                }
            }

            // Check <Setter Value="..." Property="Cursor" />
            foreach (Match match in CursorSetterReversedRegex().Matches(content))
            {
                var cursorValue = match.Groups[1].Value.Trim();
                if (!IsValidCursor(cursorValue))
                {
                    errors.Add($"{Path.GetRelativePath(solutionDir, file)}: Invalid Cursor setter value '{cursorValue}'");
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
    /// Matches XML comment blocks.
    /// </summary>
    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex XmlCommentRegex();

    /// <summary>
    /// Matches AXAML attributes of the form Cursor="value", anchored by a word boundary.
    /// </summary>
    [GeneratedRegex(@"\bCursor\s*=\s*""([^""{}]+)""", RegexOptions.IgnoreCase)]
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
