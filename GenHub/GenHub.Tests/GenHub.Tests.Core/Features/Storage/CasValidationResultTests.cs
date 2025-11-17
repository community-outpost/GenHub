using GenHub.Core.Models.Results.CAS;
using GenHub.Core.Models.Storage;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Unit tests for <see cref="CasValidationResult"/>.
/// </summary>
public class CasValidationResultTests
{
    /// <summary>
    /// Verifies that the default constructor creates a valid result.
    /// </summary>
    [Fact]
    public void DefaultConstructor_CreatesValidResult()
    {
        // Act
        var result = new CasValidationResult();

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Success);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Issues);
        Assert.Equal(0, result.ObjectsValidated);
        Assert.Equal(0, result.ObjectsWithIssues);
    }

    /// <summary>
    /// Verifies that the constructor with issues creates a valid result when no critical issues.
    /// </summary>
    [Fact]
    public void Constructor_WithNonCriticalIssues_CreatesValidResult()
    {
        // Arrange
        var issues = new List<CasValidationIssue>
        {
            new()
            {
                IssueType = CasValidationIssueType.Warning,
                Details = "Minor issue",
            },
        };

        // Act
        var result = new CasValidationResult(issues, 10);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Success);
        Assert.False(result.HasErrors);
        Assert.Single(result.Issues);
        Assert.Equal(10, result.ObjectsValidated);
        Assert.Equal(1, result.ObjectsWithIssues);
    }

    /// <summary>
    /// Verifies that the constructor with critical issues creates an invalid result.
    /// </summary>
    [Fact]
    public void Constructor_WithCriticalIssues_CreatesInvalidResult()
    {
        // Arrange
        var issues = new List<CasValidationIssue>
        {
            new()
            {
                IssueType = CasValidationIssueType.HashMismatch,
                Details = "Hash mismatch",
            },
        };

        // Act
        var result = new CasValidationResult(issues, 5);

        // Assert
        Assert.False(result.Success);
        Assert.False(result.Success);
        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Contains("HashMismatch: Hash mismatch", result.Errors);
        Assert.Single(result.Issues);
        Assert.Equal(5, result.ObjectsValidated);
        Assert.Equal(1, result.ObjectsWithIssues);
    }

    /// <summary>
    /// Verifies that multiple critical issues are handled correctly.
    /// </summary>
    [Fact]
    public void Constructor_WithMultipleCriticalIssues_CreatesInvalidResult()
    {
        // Arrange
        var issues = new List<CasValidationIssue>
        {
            new()
            {
                IssueType = CasValidationIssueType.HashMismatch,
                Details = "Hash mismatch",
            },
            new()
            {
                IssueType = CasValidationIssueType.MissingObject,
                Details = "Object missing",
            },
        };

        // Act
        var result = new CasValidationResult(issues, 20);

        // Assert
        Assert.False(result.Success);
        Assert.False(result.Success);
        Assert.True(result.HasErrors);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains("HashMismatch: Hash mismatch", result.Errors);
        Assert.Contains("MissingObject: Object missing", result.Errors);
        Assert.Equal(2, result.Issues.Count);
        Assert.Equal(20, result.ObjectsValidated);
        Assert.Equal(2, result.ObjectsWithIssues);
    }

    /// <summary>
    /// Verifies that mixed issues (critical and non-critical) are handled correctly.
    /// </summary>
    [Fact]
    public void Constructor_WithMixedIssues_CreatesInvalidResult()
    {
        // Arrange
        var issues = new List<CasValidationIssue>
        {
            new()
            {
                IssueType = CasValidationIssueType.HashMismatch,
                Details = "Hash mismatch",
            },
            new()
            {
                IssueType = CasValidationIssueType.Warning,
                Details = "Minor warning",
            },
        };

        // Act
        var result = new CasValidationResult(issues, 15);

        // Assert
        Assert.False(result.Success);
        Assert.False(result.Success);
        Assert.True(result.HasErrors);
        Assert.Single(result.Errors); // Only critical
        Assert.Contains("HashMismatch: Hash mismatch", result.Errors);
        Assert.Equal(2, result.Issues.Count);
        Assert.Equal(15, result.ObjectsValidated);
        Assert.Equal(2, result.ObjectsWithIssues);
    }
}