using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Manifest;

/// <summary>
/// Service responsible for initializing the manifest system during application startup.
/// </summary>
public class ManifestInitializationService(
    ILogger<ManifestInitializationService> logger,
    ManifestDiscoveryService discoveryService) : IHostedService
{
    /// <summary>
    /// Initializes the manifest cache during application startup.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting manifest system initialization...");

        try
        {
            await discoveryService.InitializeCacheAsync(cancellationToken);
            logger.LogInformation("Manifest system initialization completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize manifest system");
            throw;
        }
    }

    /// <summary>
    /// Performs cleanup during application shutdown.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Manifest system shutdown completed");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Refreshes the manifest cache with newly discovered content.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RefreshCacheAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Refreshing manifest cache...");
        await discoveryService.InitializeCacheAsync(cancellationToken);
        logger.LogInformation("Manifest cache refresh completed");
    }
}