using System;
using GenHub.Core.Constants;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GitHub.Services;

/// <summary>
/// Tracks GitHub API rate limits and provides time until reset.
/// </summary>
public class GitHubRateLimitTracker(ILogger<GitHubRateLimitTracker> logger)
{
    private const double WarningThreshold = GitHubConstants.DefaultRateLimitWarningThreshold;
    private int _remainingRequests = GitHubConstants.DefaultRateLimit;
    private int _totalRequests = GitHubConstants.DefaultRateLimit;
    private DateTime _resetTime = DateTime.UtcNow.AddHours(GitHubConstants.DefaultRateLimitResetHours);

    /// <summary>
    /// Gets the number of remaining API requests.
    /// </summary>
    public int RemainingRequests => _remainingRequests;

    /// <summary>
    /// Gets the total number of API requests allowed.
    /// </summary>
    public int TotalRequests => _totalRequests;

    /// <summary>
    /// Gets the time when the rate limit will reset.
    /// </summary>
    public DateTime ResetTime => _resetTime;

    /// <summary>
    /// Gets the time remaining until the rate limit resets.
    /// </summary>
    public TimeSpan TimeUntilReset => _resetTime - DateTime.UtcNow;

    /// <summary>
    /// Gets a value indicating whether the rate limit is near the threshold.
    /// </summary>
    public bool IsNearLimit => _totalRequests > 0 && _remainingRequests <= _totalRequests * (1 - WarningThreshold);

    /// <summary>
    /// Gets a value indicating whether the rate limit has been reached.
    /// </summary>
    public bool IsAtLimit => _remainingRequests <= 0;

    /// <summary>
    /// Gets the percentage of requests remaining.
    /// </summary>
    public double RemainingPercentage => _totalRequests > 0 ? (double)_remainingRequests / _totalRequests * 100 : 0;

    /// <summary>
    /// Updates rate limit information from API response headers.
    /// </summary>
    /// <param name="remaining">The remaining requests from X-RateLimit-Remaining header.</param>
    /// <param name="total">The total requests from X-RateLimit-Limit header.</param>
    /// <param name="resetTime">The reset time from X-RateLimit-Reset header.</param>
    public void UpdateFromHeaders(int remaining, int total, DateTime resetTime)
    {
        _remainingRequests = remaining;
        _totalRequests = total;
        _resetTime = resetTime;

        logger.LogInformation(
            "Rate limit updated: {Remaining}/{Total} ({Percentage}%), resets at {ResetTime}",
            remaining,
            total,
            RemainingPercentage,
            resetTime);

        if (IsNearLimit)
        {
            logger.LogWarning(
                "GitHub API rate limit near threshold: {Remaining} remaining ({Percentage}%)",
                remaining,
                RemainingPercentage);
        }
    }

    /// <summary>
    /// Updates rate limit information from a rate limit exception.
    /// </summary>
    /// <param name="resetTime">The reset time from the exception.</param>
    public void UpdateFromException(DateTime resetTime)
    {
        _remainingRequests = 0;
        _resetTime = resetTime;

        logger.LogWarning(
            "GitHub API rate limit reached. Resets at {ResetTime} ({TimeUntilReset} remaining)",
            resetTime,
            TimeUntilReset);
    }

    /// <summary>
    /// Gets a formatted string describing the current rate limit status.
    /// </summary>
    /// <returns>The formatted status string.</returns>
    public string GetStatusMessage()
    {
        if (IsAtLimit)
        {
            return $"Rate limit reached. Resets in {FormatTimeSpan(TimeUntilReset)}";
        }

        if (IsNearLimit)
        {
            return $"Rate limit warning: {RemainingPercentage:F0}% remaining ({FormatTimeSpan(TimeUntilReset)} until reset)";
        }

        return $"{RemainingRequests} requests remaining";
    }

    /// <summary>
    /// Formats a time span for display.
    /// </summary>
    /// <param name="span">The time span to format.</param>
    /// <returns>The formatted time string.</returns>
    private static string FormatTimeSpan(TimeSpan span)
    {
        if (span.TotalHours < 1)
        {
            return $"{span.Minutes}m";
        }
        else if (span.TotalHours < 24)
        {
            return $"{span.Hours}h {span.Minutes}m";
        }
        else
        {
            return $"{span.Days}d {span.Hours}h";
        }
    }
}
