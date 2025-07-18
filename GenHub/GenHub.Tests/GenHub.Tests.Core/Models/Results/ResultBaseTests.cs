using System;
using System.Collections.Generic;
using GenHub.Core.Models.Results;
using Xunit;

namespace GenHub.Tests.Core.Models.Results;

/// <summary>
/// Unit tests for <see cref="ResultBase"/>.
/// </summary>
public class ResultBaseTests
{
    /// <summary>
    /// Verifies that the constructor sets properties correctly for a successful result.
    /// </summary>
    [Fact]
    public void Constructor_WithSuccess_SetsPropertiesCorrectly()
    {
        // Arrange
        var elapsed = TimeSpan.FromSeconds(5);
        var beforeCreation = DateTime.UtcNow;

        // Act
        var result = new TestResult(true, error: null, elapsed: elapsed);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Failed);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Null(result.FirstError);
        Assert.Equal(string.Empty, result.AllErrors);
        Assert.Equal(elapsed, result.Elapsed);
        Assert.Equal(5000, result.ElapsedMilliseconds);
        Assert.Equal(5, result.ElapsedSeconds);
        Assert.True(result.CompletedAt >= beforeCreation);
        Assert.True(result.CompletedAt <= DateTime.UtcNow);
    }

    /// <summary>
    /// Verifies that the constructor sets properties correctly for a single error.
    /// </summary>
    [Fact]
    public void Constructor_WithSingleError_SetsPropertiesCorrectly()
    {
        // Arrange
        var error = "Test error";
        var elapsed = TimeSpan.FromMilliseconds(100);

        // Act
        var result = new TestResult(false, error: error, elapsed: elapsed);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.Failed);
        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal(error, result.FirstError);
        Assert.Equal(error, result.AllErrors);
        Assert.Equal(elapsed, result.Elapsed);
    }

    /// <summary>
    /// Verifies that the constructor sets properties correctly for multiple errors.
    /// </summary>
    [Fact]
    public void Constructor_WithMultipleErrors_SetsPropertiesCorrectly()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2", "Error 3" };
        var elapsed = TimeSpan.FromSeconds(2);

        // Act
        var result = new TestResult(false, errors: errors, elapsed: elapsed);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.Failed);
        Assert.True(result.HasErrors);
        Assert.Equal(3, result.Errors.Count);
        Assert.Equal("Error 1", result.FirstError);
        Assert.Equal($"Error 1{Environment.NewLine}Error 2{Environment.NewLine}Error 3", result.AllErrors);
        Assert.Equal(elapsed, result.Elapsed);
    }

    /// <summary>
    /// Verifies that the constructor handles null errors gracefully.
    /// </summary>
    [Fact]
    public void Constructor_WithNullErrors_HandlesGracefully()
    {
        // Act
        var result = new TestResult(true, errors: (IEnumerable<string>?)null);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Null(result.FirstError);
        Assert.Equal(string.Empty, result.AllErrors);
    }

    /// <summary>
    /// Verifies that the constructor handles empty errors gracefully.
    /// </summary>
    [Fact]
    public void Constructor_WithEmptyErrors_HandlesGracefully()
    {
        // Act
        var result = new TestResult(true, errors: Array.Empty<string>());

        // Assert
        Assert.True(result.Success);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Null(result.FirstError);
        Assert.Equal(string.Empty, result.AllErrors);
    }

    /// <summary>
    /// Verifies that the elapsed time properties calculate the correct values.
    /// </summary>
    /// <param name="milliseconds">The elapsed time in milliseconds.</param>
    /// <param name="expectedMs">The expected elapsed milliseconds.</param>
    /// <param name="expectedSeconds">The expected elapsed seconds.</param>
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1000, 1000, 1)]
    [InlineData(5500, 5500, 5.5)]
    [InlineData(60000, 60000, 60)]
    public void ElapsedTimeProperties_CalculateCorrectly(long milliseconds, double expectedMs, double expectedSeconds)
    {
        // Arrange
        var elapsed = TimeSpan.FromMilliseconds(milliseconds);

        // Act
        var result = new TestResult(true, error: null, elapsed: elapsed);

        // Assert
        Assert.Equal(expectedMs, result.ElapsedMilliseconds);
        Assert.Equal(expectedSeconds, result.ElapsedSeconds);
    }

    private class TestResult : ResultBase
    {
        public TestResult(bool success, string? error = null, TimeSpan elapsed = default)
            : base(success, error, elapsed)
        {
        }

        public TestResult(bool success, IEnumerable<string>? errors = null, TimeSpan elapsed = default)
            : base(success, errors, elapsed)
        {
        }
    }
}
