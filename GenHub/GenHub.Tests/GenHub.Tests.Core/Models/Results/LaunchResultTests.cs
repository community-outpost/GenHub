using GenHub.Core.Models.Results;

namespace GenHub.Tests.Core.Models.Results;

/// <summary>
/// Unit tests for <see cref="LaunchResult"/>.
/// </summary>
public class LaunchResultTests
{
    /// <summary>
    /// Verifies that CreateSuccess creates a successful result with valid parameters.
    /// </summary>
    [Fact]
    public void CreateSuccess_WithValidParameters_CreatesSuccessfulResult()
    {
        // Arrange
        var processId = 1234;
        var startTime = DateTime.UtcNow;
        var launchDuration = TimeSpan.FromSeconds(5);

        // Act
        var result = LaunchResult.CreateSuccess(processId, startTime, launchDuration);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Failed);
        Assert.Equal(processId, result.ProcessId);
        Assert.Equal(startTime, result.StartTime);
        Assert.Equal(launchDuration, result.LaunchDuration);
        Assert.Equal(launchDuration, result.Elapsed);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Errors);
        Assert.Null(result.FirstError);
        Assert.Null(result.Exception);
    }

    /// <summary>
    /// Verifies that CreateFailure creates a failed result with error message.
    /// </summary>
    [Fact]
    public void CreateFailure_WithErrorMessage_CreatesFailedResult()
    {
        // Arrange
        var errorMessage = "Launch failed";

        // Act
        var result = LaunchResult.CreateFailure(errorMessage);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.Failed);
        Assert.Equal(errorMessage, result.FirstError);
        Assert.Equal(errorMessage, result.FirstError);
        Assert.Contains(errorMessage, result.Errors);
        Assert.True(result.HasErrors);
        Assert.Null(result.ProcessId);
        Assert.Null(result.Exception);
    }

    /// <summary>
    /// Verifies that CreateFailure creates a failed result with error message and exception.
    /// </summary>
    [Fact]
    public void CreateFailure_WithErrorMessageAndException_CreatesFailedResult()
    {
        // Arrange
        var errorMessage = "Launch failed";
        var exception = new Exception("Test exception");

        // Act
        var result = LaunchResult.CreateFailure(errorMessage, exception);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.Failed);
        Assert.Equal(errorMessage, result.FirstError);
        Assert.Equal(exception, result.Exception);
        Assert.Contains(errorMessage, result.Errors);
        Assert.True(result.HasErrors);
        Assert.Null(result.ProcessId);
    }

    /// <summary>
    /// Verifies that the constructor sets properties correctly for a successful launch.
    /// </summary>
    [Fact]
    public void Constructor_WithSuccess_SetsPropertiesCorrectly()
    {
        // Arrange
        var processId = 5678;
        var startTime = DateTime.UtcNow.AddMinutes(-1);
        var launchDuration = TimeSpan.FromSeconds(10);

        // Act
        var result = new LaunchResult(true, processId, null, null, launchDuration, startTime);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(processId, result.ProcessId);
        Assert.Equal(startTime, result.StartTime);
        Assert.Equal(launchDuration, result.LaunchDuration);
        Assert.Equal(launchDuration, result.Elapsed);
        Assert.False(result.HasErrors);
    }

    /// <summary>
    /// Verifies that the constructor sets properties correctly for a failed launch.
    /// </summary>
    [Fact]
    public void Constructor_WithFailure_SetsPropertiesCorrectly()
    {
        // Arrange
        var errorMessage = "Failed to launch";
        var exception = new InvalidOperationException("Invalid operation");
        var launchDuration = TimeSpan.FromSeconds(2);

        // Act
        var result = new LaunchResult(false, null, errorMessage, exception, launchDuration, DateTime.UtcNow);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(errorMessage, result.FirstError);
        Assert.Equal(exception, result.Exception);
        Assert.Equal(launchDuration, result.LaunchDuration);
        Assert.True(result.HasErrors);
        Assert.Null(result.ProcessId);
    }
}