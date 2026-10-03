using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Models.Results;

/// <summary>
/// Unit tests for <see cref="DependencyResolutionResult"/>.
/// </summary>
public class DependencyResolutionResultTests
{
    /// <summary>
    /// Verifies that CreateSuccess sets warnings to an empty array.
    /// </summary>
    [Fact]
    public void CreateSuccess_WarningsProperty_IsEmptyArray()
    {
        // Act
        var result = DependencyResolutionResult.CreateSuccess(
            Array.Empty<string>(),
            Array.Empty<ContentManifest>(),
            Array.Empty<string>());

        // Assert
        Assert.NotNull(result.Warnings);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// Verifies that CreateSuccess sets success to true.
    /// </summary>
    [Fact]
    public void CreateSuccess_SuccessProperty_IsTrue()
    {
        // Act
        var result = DependencyResolutionResult.CreateSuccess(
            Array.Empty<string>(),
            Array.Empty<ContentManifest>(),
            Array.Empty<string>());

        // Assert
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies that CreateSuccess sets all properties correctly.
    /// </summary>
    [Fact]
    public void CreateSuccess_SetsAllPropertiesCorrectly()
    {
        // Arrange
        var contentIds = new[] { "content1", "content2" };
        var manifests = new[]
        {
            new ContentManifest { Id = "1.108.test.gameinstallation.testcontent1" },
            new ContentManifest { Id = "1.108.test.gameinstallation.testcontent2" },
        };
        var missingIds = new[] { "missing1" };
        var elapsed = TimeSpan.FromMilliseconds(123);

        // Act
        var result = DependencyResolutionResult.CreateSuccess(contentIds, manifests, missingIds, elapsed);

        // Assert
        Assert.Equal(contentIds, result.ResolvedContentIds);
        Assert.Equal(manifests, result.ResolvedManifests);
        Assert.Equal(missingIds, result.MissingContentIds);
        Assert.Equal(elapsed, result.Elapsed);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// Verifies that CreateSuccessWithWarnings sets warnings correctly.
    /// </summary>
    [Fact]
    public void CreateSuccessWithWarnings_WarningsProperty_ContainsExpectedValues()
    {
        // Arrange
        var warnings = new[] { "Warning 1", "Warning 2" };

        // Act
        var result = DependencyResolutionResult.CreateSuccessWithWarnings(
            Array.Empty<string>(),
            Array.Empty<ContentManifest>(),
            Array.Empty<string>(),
            warnings);

        // Assert
        Assert.Equal(2, result.Warnings.Count);
        Assert.Equal(warnings, result.Warnings);
    }

    /// <summary>
    /// Verifies that CreateSuccessWithWarnings sets success to true.
    /// </summary>
    [Fact]
    public void CreateSuccessWithWarnings_SuccessProperty_IsTrue()
    {
        // Arrange
        var warnings = new[] { "Warning 1" };

        // Act
        var result = DependencyResolutionResult.CreateSuccessWithWarnings(
            Array.Empty<string>(),
            Array.Empty<ContentManifest>(),
            Array.Empty<string>(),
            warnings);

        // Assert
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies that CreateFailure with single error sets warnings to empty array.
    /// </summary>
    [Fact]
    public void CreateFailure_WithSingleError_WarningsProperty_IsEmptyArray()
    {
        // Act
        var result = DependencyResolutionResult.CreateFailure("Test error");

        // Assert
        Assert.NotNull(result.Warnings);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// Verifies that CreateFailure with single error sets success to false.
    /// </summary>
    [Fact]
    public void CreateFailure_WithSingleError_SuccessProperty_IsFalse()
    {
        // Act
        var result = DependencyResolutionResult.CreateFailure("Test error");

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that CreateFailure with single error sets errors correctly.
    /// </summary>
    [Fact]
    public void CreateFailure_WithSingleError_SetsErrors()
    {
        // Arrange
        const string errorMessage = "Test error";

        // Act
        var result = DependencyResolutionResult.CreateFailure(errorMessage);

        // Assert
        var error = Assert.Single(result.Errors);
        Assert.Equal(errorMessage, error);
    }

    /// <summary>
    /// Verifies that CreateFailure with single error and elapsed sets elapsed correctly.
    /// </summary>
    [Fact]
    public void CreateFailure_WithSingleError_SetsElapsed()
    {
        // Arrange
        var elapsed = TimeSpan.FromMilliseconds(456);

        // Act
        var result = DependencyResolutionResult.CreateFailure("Test error", elapsed);

        // Assert
        Assert.Equal(elapsed, result.Elapsed);
    }

    /// <summary>
    /// Verifies that CreateFailure with multiple errors sets warnings to empty array.
    /// </summary>
    [Fact]
    public void CreateFailure_WithMultipleErrors_WarningsProperty_IsEmptyArray()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2", "Error 3" };

        // Act
        var result = DependencyResolutionResult.CreateFailure(errors);

        // Assert
        Assert.NotNull(result.Warnings);
        Assert.Empty(result.Warnings);
    }

    /// <summary>
    /// Verifies that CreateFailure with multiple errors sets success to false.
    /// </summary>
    [Fact]
    public void CreateFailure_WithMultipleErrors_SuccessProperty_IsFalse()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2" };

        // Act
        var result = DependencyResolutionResult.CreateFailure(errors);

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that CreateFailure with multiple errors sets all errors correctly.
    /// </summary>
    [Fact]
    public void CreateFailure_WithMultipleErrors_SetsAllErrors()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2", "Error 3" };

        // Act
        var result = DependencyResolutionResult.CreateFailure(errors);

        // Assert
        Assert.Equal(errors, result.Errors);
    }

    /// <summary>
    /// Verifies that CreateFailure with multiple errors and elapsed sets elapsed correctly.
    /// </summary>
    [Fact]
    public void CreateFailure_WithMultipleErrors_AndElapsed_SetsElapsed()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2" };
        var elapsed = TimeSpan.FromMilliseconds(789);

        // Act
        var result = DependencyResolutionResult.CreateFailure(errors, elapsed);

        // Assert
        Assert.Equal(elapsed, result.Elapsed);
    }

    /// <summary>
    /// Verifies that CreateFailure with null errors throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void CreateFailure_WithMultipleErrors_NullErrors_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => DependencyResolutionResult.CreateFailure((IEnumerable<string>)null!));
    }

    /// <summary>
    /// Verifies that CreateFailure with empty errors throws ArgumentException.
    /// </summary>
    [Fact]
    public void CreateFailure_WithMultipleErrors_EmptyErrors_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => DependencyResolutionResult.CreateFailure(Enumerable.Empty<string>()));
        Assert.Equal("Errors collection cannot be empty. (Parameter 'errors')", exception.Message);
    }
}
