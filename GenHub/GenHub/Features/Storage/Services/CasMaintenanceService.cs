using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenHub.Features.Storage.Services;

/// <summary>
/// Background service for CAS maintenance tasks like garbage collection.
/// </summary>
public class CasMaintenanceService(
    IServiceProvider serviceProvider,
    IOptions<CasConfiguration> config,
    ILogger<CasMaintenanceService> logger) : BackgroundService
{
    private const int ErrorRetryDelayMinutes = 5;
    private readonly CasConfiguration _config = config.Value;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.EnableAutomaticGc)
        {
            logger.LogInformation("Automatic CAS garbage collection is disabled");
            return;
        }

        logger.LogInformation("CAS maintenance service started with interval: {Interval}", _config.AutoGcInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_config.AutoGcInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                await RunMaintenanceTasksAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Service is stopping
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during CAS maintenance cycle");

                // Continue with next cycle after a delay
                await Task.Delay(TimeSpan.FromMinutes(ErrorRetryDelayMinutes), stoppingToken);
            }
        }

        logger.LogInformation("CAS maintenance service stopped");
    }

    private static bool ShouldRunIntegrityValidation()
    {
        // Run integrity validation once per week
        return DateTime.UtcNow.DayOfWeek == DayOfWeek.Sunday;
    }

    private async Task RunMaintenanceTasksAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var casService = scope.ServiceProvider.GetRequiredService<ICasService>();

        logger.LogDebug("Starting CAS maintenance tasks");

        // Run garbage collection
        var gcResult = await casService.RunGarbageCollectionAsync(cancellationToken: cancellationToken);

        if (gcResult.Success)
        {
            logger.LogInformation("CAS garbage collection completed: {ObjectsDeleted} objects deleted, {BytesFreed:N0} bytes freed in {Elapsed}", gcResult.ObjectsDeleted, gcResult.BytesFreed, gcResult.Elapsed);
        }
        else
        {
            logger.LogWarning("CAS garbage collection failed: {ErrorMessage}", gcResult.FirstError);
        }

        // Optionally run integrity validation periodically
        if (ShouldRunIntegrityValidation())
        {
            logger.LogDebug("Running CAS integrity validation");
            var validationResult = await casService.ValidateIntegrityAsync(cancellationToken);

            if (validationResult.Success)
            {
                logger.LogInformation("CAS integrity validation passed: {ObjectsValidated} objects validated", validationResult.ObjectsValidated);
            }
            else
            {
                logger.LogWarning("CAS integrity validation found {IssueCount} issues in {ObjectsValidated} objects", validationResult.ObjectsWithIssues, validationResult.ObjectsValidated);
            }
        }
    }
}
