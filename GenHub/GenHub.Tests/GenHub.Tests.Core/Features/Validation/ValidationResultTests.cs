using System.Collections.Generic;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Validation;
using Xunit;

namespace GenHub.Tests.Features.Validation;

/// <summary>
/// Contains unit tests for the <see cref="ValidationResult"/> class.
/// </summary>
public class ValidationResultTests
{
    /// <summary>
    /// Verifies that the constructor initializes properties correctly with valid parameters.
    /// </summary>
    [Fact]
    public void Constructor_InitializesProperties_Correctly()
    {
        // Arrange
        var issues = new List<ValidationIssue> { new() { IssueType = ValidationIssueType.MissingFile } };

        // Act
        var result = new ValidationResult("test-id", issues);

        // Assert
        Assert.Equal("test-id", result.ValidatedTargetId);
        Assert.Single(result.Issues);
    }

    /// <summary>
    /// Verifies that <see cref="ValidationResult.IsValid"/> returns the expected value based on issue type.
    /// </summary>
    /// <param name="issueType">The type of validation issue.</param>
    /// <param name="expectedValid">Whether the result should be valid.</param>
    [Theory]
    [InlineData(ValidationIssueType.MissingFile, false)] // Critical issue
    [InlineData(ValidationIssueType.AddonDetected, true)] // Non-critical
    public void IsValid_ReturnsExpected_BasedOnIssues(ValidationIssueType issueType, bool expectedValid)
    {
        // Arrange
        var issues = new List<ValidationIssue> { new() { IssueType = issueType } };

        // Act
        var result = new ValidationResult("id", issues);

        // Assert
        Assert.Equal(expectedValid, result.IsValid);
    }

    /// <summary>
    /// Verifies that <see cref="ValidationResult.IsValid"/> returns true when there are no issues.
    /// </summary>
    [Fact]
    public void IsValid_EmptyIssues_ReturnsTrue()
    {
        var result = new ValidationResult("id", new List<ValidationIssue>());
        Assert.True(result.IsValid);
    }
}
