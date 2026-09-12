using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Abstract base class for content update services.
/// Provides common background service implementation for periodic update checking.
/// </summary>
public abstract class ContentUpdateServiceBase(ILogger<ContentUpdateServiceBase> logger) : BackgroundService, IContentUpdateService
{
    /// <summary>
    /// Gets the name of the update service for logging purposes.
    /// </summary>
    protected abstract string ServiceName { get; }

    /// <summary>
    /// Gets the interval between update checks.
    /// </summary>
    protected abstract TimeSpan UpdateCheckInterval { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Checks for available content updates.
    /// </remarks>
    public abstract Task<ContentUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Executes the background service that periodically checks for updates.
    /// </summary>
    /// <param name="stoppingToken">
    /// Cancellation token for stopping the service (provided by BackgroundService base class).
    /// Named 'stoppingToken' to match .NET framework convention for hosted services.
    /// </param>
    /// <returns>A task representing the background execution.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[{ServiceName}] Update service started. Check interval: {Interval}", ServiceName, UpdateCheckInterval);

        // Initial delay to allow application to fully start
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan nextDelay = UpdateCheckInterval;

            try
            {
                logger.LogDebug("[{ServiceName}] Starting update check", ServiceName);

                var result = await CheckForUpdatesAsync(stoppingToken);

                if (!result.Success)
                {
                    nextDelay = TimeSpan.FromMinutes(30); // Retry sooner on failure
                    logger.LogWarning(
                        "[{ServiceName}] Update check failed: {Error}. Will retry in {Interval}",
                        ServiceName,
                        result.FirstError ?? "Unknown error",
                        nextDelay);
                }
                else if (result.IsUpdateAvailable)
                {
                    logger.LogInformation(
                        "[{ServiceName}] Update available: {LatestVersion} (current: {CurrentVersion})",
                        ServiceName,
                        result.LatestVersion ?? "unknown",
                        result.CurrentVersion ?? "none");
                }
                else
                {
                    logger.LogDebug(
                        "[{ServiceName}] No updates available. Current version: {CurrentVersion}",
                        ServiceName,
                        result.CurrentVersion ?? "none");
                }
            }
            catch (OperationCanceledException)
            {
                // Service is being stopped, exit gracefully
                logger.LogInformation("[{ServiceName}] Update service stopped", ServiceName);
                break;
            }
            catch (Exception ex)
            {
                nextDelay = TimeSpan.FromMinutes(30); // Retry sooner on failure
                logger.LogError(
                    ex,
                    "[{ServiceName}] Update check failed with exception. Will retry in {Interval}",
                    ServiceName,
                    nextDelay);
            }

            // Wait for the configured interval before next check
            try
            {
                await Task.Delay(nextDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Service is being stopped
                logger.LogInformation("[{ServiceName}] Update service stopped", ServiceName);
                break;
            }
        }

        logger.LogInformation("[{ServiceName}] Update service shutdown complete", ServiceName);
    }
}
