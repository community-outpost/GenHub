using System;
using GenHub.Core.Models.Results.CAS;
using Xunit;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Unit tests for <see cref="CasGarbageCollectionResult"/>.
/// </summary>
public class CasGarbageCollectionResultTests
{
    /// <summary>
    /// Verifies that the constructor sets properties correctly for a successful result.
    /// </summary>
    [Fact]
    public void Constructor_WithSuccess_SetsPropertiesCorrectly()
    {
        // Arrange
        var elapsed = TimeSpan.FromSeconds(10);

        // Act
        var result = new CasGarbageCollectionResult(true, (string?)null, elapsed);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Failed);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Equal(elapsed, result.Elapsed);
    }

    /// <summary>
    /// Verifies that the constructor sets properties correctly for a failed result.
    /// </summary>
    [Fact]
    public void Constructor_WithFailure_SetsPropertiesCorrectly()
    {
        // Arrange
        var error = "Collection failed";
        var elapsed = TimeSpan.FromSeconds(5);

        // Act
        var result = new CasGarbageCollectionResult(false, error, elapsed);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.Failed);
        Assert.True(result.HasErrors);
        Assert.Single(result.Errors);
        Assert.Equal(error, result.FirstError);
        Assert.Equal(elapsed, result.Elapsed);
    }

    /// <summary>
    /// Verifies that PercentageFreed calculates correctly when objects are deleted.
    /// </summary>
    [Fact]
    public void PercentageFreed_WithValidData_CalculatesCorrectly()
    {
        // Arrange
        var result = new CasGarbageCollectionResult(true, (string?)null);
        result.ObjectsScanned = 100;
        result.ObjectsDeleted = 25;

        // Act
        var percentage = result.PercentageFreed;

        // Assert
        Assert.Equal(25.0, percentage);
    }

    /// <summary>
    /// Verifies that PercentageFreed returns 0 when no objects scanned.
    /// </summary>
    [Fact]
    public void PercentageFreed_WithZeroScanned_ReturnsZero()
    {
        // Arrange
        var result = new CasGarbageCollectionResult(true, (string?)null);
        result.ObjectsScanned = 0;
        result.ObjectsDeleted = 10;

        // Act
        var percentage = result.PercentageFreed;

        // Assert
        Assert.Equal(0.0, percentage);
    }

    /// <summary>
    /// Verifies that PercentageFreed returns 0 when negative objects deleted.
    /// </summary>
    [Fact]
    public void PercentageFreed_WithNegativeDeleted_ReturnsZero()
    {
        // Arrange
        var result = new CasGarbageCollectionResult(true, (string?)null);
        result.ObjectsScanned = 100;
        result.ObjectsDeleted = -5;

        // Act
        var percentage = result.PercentageFreed;

        // Assert
        Assert.Equal(0.0, percentage);
    }

    /// <summary>
    /// Verifies that PercentageFreed returns 100 when deleted more than scanned.
    /// </summary>
    [Fact]
    public void PercentageFreed_WithDeletedMoreThanScanned_Returns100()
    {
        // Arrange
        var result = new CasGarbageCollectionResult(true, (string?)null);
        result.ObjectsScanned = 50;
        result.ObjectsDeleted = 75;

        // Act
        var percentage = result.PercentageFreed;

        // Assert
        Assert.Equal(100.0, percentage);
    }

    /// <summary>
    /// Verifies that properties can be set correctly.
    /// </summary>
    [Fact]
    public void Properties_CanBeSetCorrectly()
    {
        // Arrange
        var result = new CasGarbageCollectionResult(true, (string?)null);

        // Act
        result.ObjectsDeleted = 10;
        result.BytesFreed = 1024;
        result.ObjectsScanned = 50;
        result.ObjectsReferenced = 40;

        // Assert
        Assert.Equal(10, result.ObjectsDeleted);
        Assert.Equal(1024, result.BytesFreed);
        Assert.Equal(50, result.ObjectsScanned);
        Assert.Equal(40, result.ObjectsReferenced);
        Assert.Equal(20.0, result.PercentageFreed);
    }
}
