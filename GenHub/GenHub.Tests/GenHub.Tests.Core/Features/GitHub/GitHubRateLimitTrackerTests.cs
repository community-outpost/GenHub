using System;
using System.Collections.Generic;
using GenHub.Core.Exceptions;
using GenHub.Features.GitHub.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Features.GitHub;

/// <summary>
/// Contains unit tests for <see cref="GitHubRateLimitTracker"/> class.
/// </summary>
public class GitHubRateLimitTrackerTests
{
    /// <summary>
    /// Verifies that constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesProperties_Correctly()
    {
        // Arrange & Act
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);

        // Assert
        Assert.Equal(5000, tracker.RemainingRequests);
        Assert.Equal(5000, tracker.TotalRequests);
        Assert.Equal(DateTime.UtcNow.AddHours(1).Ticks, tracker.ResetTime.Ticks, TimeSpan.FromSeconds(1).Ticks);
        Assert.Equal(TimeSpan.FromHours(1).TotalSeconds, tracker.TimeUntilReset.TotalSeconds, 1);
        Assert.False(tracker.IsNearLimit);
        Assert.False(tracker.IsAtLimit);
        Assert.Equal(100.0, tracker.RemainingPercentage);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.UpdateFromHeaders"/> parses rate limit headers correctly.
    /// </summary>
    [Fact]
    public void UpdateFromHeaders_ParsesHeaders_Correctly()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);
        var expectedResetTime = new DateTime(2009, 2, 13, 23, 31, 0, DateTimeKind.Utc);

        // Act
        tracker.UpdateFromHeaders(30, 60, expectedResetTime);

        // Assert
        Assert.Equal(60, tracker.TotalRequests);
        Assert.Equal(30, tracker.RemainingRequests);
        Assert.Equal(expectedResetTime.Ticks, tracker.ResetTime.Ticks, TimeSpan.FromSeconds(1).Ticks);
        Assert.Equal(50.0, tracker.RemainingPercentage);
        Assert.False(tracker.IsNearLimit);
        Assert.False(tracker.IsAtLimit);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.UpdateFromHeaders"/> handles missing headers gracefully.
    /// </summary>
    [Fact]
    public void UpdateFromHeaders_HandlesMissingHeaders_Gracefully()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);

        // Act
        tracker.UpdateFromHeaders(0, 0, DateTime.UtcNow);

        // Assert - Should not throw and keep default values
        Assert.Equal(0, tracker.TotalRequests);
        Assert.Equal(0, tracker.RemainingRequests);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.UpdateFromException"/> parses exception correctly.
    /// </summary>
    [Fact]
    public void UpdateFromException_ParsesException_Correctly()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);

        var expectedResetTime = new DateTime(2009, 2, 13, 23, 31, 0, DateTimeKind.Utc);

        // Act
        tracker.UpdateFromException(expectedResetTime);

        // Assert
        Assert.Equal(5000, tracker.TotalRequests);
        Assert.Equal(0, tracker.RemainingRequests);
        Assert.Equal(expectedResetTime.Ticks, tracker.ResetTime.Ticks, TimeSpan.FromSeconds(1).Ticks);
        Assert.Equal(0.0, tracker.RemainingPercentage);
        Assert.True(tracker.IsAtLimit);
        Assert.True(tracker.IsNearLimit);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.IsNearLimit"/> returns true when below threshold.
    /// </summary>
    [Fact]
    public void IsNearLimit_ReturnsTrue_WhenBelowThreshold()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);
        var expectedResetTime = DateTime.UtcNow.AddHours(1);
        tracker.UpdateFromHeaders(9, 100, expectedResetTime); // 9% - below 10% threshold

        // Assert
        Assert.True(tracker.IsNearLimit);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.IsNearLimit"/> returns false when above threshold.
    /// </summary>
    [Fact]
    public void IsNearLimit_ReturnsFalse_WhenAboveThreshold()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);
        var expectedResetTime = DateTime.UtcNow.AddHours(1);
        tracker.UpdateFromHeaders(11, 100, expectedResetTime); // 11% - above 10% threshold

        // Assert
        Assert.False(tracker.IsNearLimit);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.IsAtLimit"/> returns true when at limit.
    /// </summary>
    [Fact]
    public void IsAtLimit_ReturnsTrue_WhenAtLimit()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);
        var expectedResetTime = DateTime.UtcNow.AddHours(1);
        tracker.UpdateFromHeaders(0, 100, expectedResetTime);

        // Assert
        Assert.True(tracker.IsAtLimit);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.IsAtLimit"/> returns false when not at limit.
    /// </summary>
    [Fact]
    public void IsAtLimit_ReturnsFalse_WhenNotAtLimit()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);
        var expectedResetTime = DateTime.UtcNow.AddHours(1);
        tracker.UpdateFromHeaders(1, 100, expectedResetTime);

        // Assert
        Assert.False(tracker.IsAtLimit);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.RemainingPercentage"/> is calculated correctly.
    /// </summary>
    [Fact]
    public void RemainingPercentage_CalculatesCorrectly()
    {
        // Arrange
        var logger = new NullLogger<GitHubRateLimitTracker>();
        var tracker = new GitHubRateLimitTracker(logger);
        var expectedResetTime = DateTime.UtcNow.AddHours(1);
        tracker.UpdateFromHeaders(50, 100, expectedResetTime);

        // Assert
        Assert.Equal(50.0, tracker.RemainingPercentage);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.TimeUntilReset"/> is calculated correctly.
    /// </summary>
    [Fact]
    public void TimeUntilReset_CalculatesCorrectly()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);
        var resetTime = DateTime.UtcNow.AddHours(1);
        tracker.UpdateFromHeaders(50, 100, resetTime);

        // Assert
        Assert.InRange(tracker.TimeUntilReset.TotalMinutes, 59, 61); // Allow for 1 minute variance
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.GetStatusMessage"/> returns appropriate message.
    /// </summary>
    [Fact]
    public void GetStatusMessage_ReturnsAppropriateMessage()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);
        var expectedResetTime = DateTime.UtcNow.AddHours(1);
        tracker.UpdateFromHeaders(5, 100, expectedResetTime); // 5% - near limit
        var message = tracker.GetStatusMessage();

        // Assert
        Assert.NotNull(message);
        Assert.Contains("5%", message);
        Assert.Contains("remaining", message);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.GetStatusMessage"/> returns limit reached message.
    /// </summary>
    [Fact]
    public void GetStatusMessage_ReturnsLimitReachedMessage()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);
        var expectedResetTime = DateTime.UtcNow.AddHours(1);
        tracker.UpdateFromHeaders(0, 100, expectedResetTime);
        var message = tracker.GetStatusMessage();

        // Assert
        Assert.NotNull(message);
        Assert.Contains("Rate limit reached", message);
    }

    /// <summary>
    /// Verifies that <see cref="GitHubRateLimitTracker.GetStatusMessage"/> returns warning message.
    /// </summary>
    [Fact]
    public void GetStatusMessage_ReturnsWarningMessage()
    {
        // Arrange
        var logger = NullLogger<GitHubRateLimitTracker>.Instance;
        var tracker = new GitHubRateLimitTracker(logger);
        var expectedResetTime = DateTime.UtcNow.AddHours(1);
        tracker.UpdateFromHeaders(9, 100, expectedResetTime);
        var message = tracker.GetStatusMessage();

        // Assert
        Assert.NotNull(message);
        Assert.Contains("Rate limit warning", message);
    }
}