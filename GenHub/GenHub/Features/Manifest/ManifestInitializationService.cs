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
    private readonly ILogger<ManifestInitializationService> _logger = logger;
    private readonly ManifestDiscoveryService _discoveryService = discoveryService;

    /// <summary>
    /// Initializes the manifest cache during application startup.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting manifest system initialization...");

        try
        {
            await _discoveryService.InitializeCacheAsync(cancellationToken);
            _logger.LogInformation("Manifest system initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize manifest system");
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
        _logger.LogInformation("Manifest system shutdown completed");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Refreshes the manifest cache with newly discovered content.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RefreshCacheAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Refreshing manifest cache...");
        await _discoveryService.InitializeCacheAsync(cancellationToken);
        _logger.LogInformation("Manifest cache refresh completed");
    }
}