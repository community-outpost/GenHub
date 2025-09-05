using System;
using System.Collections.Generic;
using GenHub.Core.Models.Results;
using Xunit;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Unit tests for <see cref="OperationResult{T}"/> focusing on multi-error handling and propagation.
/// </summary>
public class OperationResultTests
{
    /// <summary>
    /// Verifies that CreateFailure with multiple errors preserves error messages.
    /// </summary>
    [Fact]
    public void CreateFailure_WithMultipleErrors_PreservesErrors()
    {
        // Arrange
        var errors = new[] { "First error", "Second error", "Third error" };

        // Act
        var result = OperationResult<string>.CreateFailure(errors);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Errors);
        Assert.Equal(3, result.Errors.Count);
        Assert.Equal("First error", result.FirstError);
        Assert.Contains("Second error", result.Errors);
        Assert.Equal(string.Join(Environment.NewLine, errors), result.AllErrors);
    }

    /// <summary>
    /// Verifies that creating a failure from another <see cref="ResultBase"/> copies all underlying errors.
    /// </summary>
    [Fact]
    public void CreateFailure_FromResultBase_CopiesAllErrors()
    {
        // Arrange
        var errors = new List<string> { "a", "b", "c" };
        var src = new TestResult(errors);

        // Act
        var result = OperationResult<int>.CreateFailure(src);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(3, result.Errors.Count);
        Assert.Equal("a", result.FirstError);
    }

    /// <summary>
    /// Simple <see cref="ResultBase"/> implementation used for testing error propagation.
    /// </summary>
    private sealed class TestResult : ResultBase
    {
        public TestResult(IEnumerable<string> errors)
            : base(false, errors)
        {
        }
    }
}
