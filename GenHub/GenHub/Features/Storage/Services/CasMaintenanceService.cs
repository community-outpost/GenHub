using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

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
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly CasConfiguration _config = config.Value;
    private readonly ILogger<CasMaintenanceService> _logger = logger;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.EnableAutomaticGc)
        {
            _logger.LogInformation("Automatic CAS garbage collection is disabled");
            return;
        }

        _logger.LogInformation("CAS maintenance service started with interval: {Interval}", _config.AutoGcInterval);

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
                _logger.LogError(ex, "Error during CAS maintenance cycle");

                // Continue with next cycle after a delay
                await Task.Delay(TimeSpan.FromMinutes(ErrorRetryDelayMinutes), stoppingToken);
            }
        }

        _logger.LogInformation("CAS maintenance service stopped");
    }

    private async Task RunMaintenanceTasksAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var casService = scope.ServiceProvider.GetRequiredService<ICasService>();

        _logger.LogDebug("Starting CAS maintenance tasks");

        // Run garbage collection
        var gcResult = await casService.RunGarbageCollectionAsync(cancellationToken);

        if (gcResult.Success)
        {
            _logger.LogInformation("CAS garbage collection completed: {ObjectsDeleted} objects deleted, {BytesFreed:N0} bytes freed in {Elapsed}", gcResult.ObjectsDeleted, gcResult.BytesFreed, gcResult.Elapsed);
        }
        else
        {
            _logger.LogWarning("CAS garbage collection failed: {ErrorMessage}", gcResult.FirstError);
        }

        // Optionally run integrity validation periodically
        if (ShouldRunIntegrityValidation())
        {
            _logger.LogDebug("Running CAS integrity validation");
            var validationResult = await casService.ValidateIntegrityAsync(cancellationToken);

            if (validationResult.IsValid)
            {
                _logger.LogInformation("CAS integrity validation passed: {ObjectsValidated} objects validated", validationResult.ObjectsValidated);
            }
            else
            {
                _logger.LogWarning("CAS integrity validation found {IssueCount} issues in {ObjectsValidated} objects", validationResult.ObjectsWithIssues, validationResult.ObjectsValidated);
            }
        }
    }

    private bool ShouldRunIntegrityValidation()
    {
        // Run integrity validation once per week
        return DateTime.UtcNow.DayOfWeek == DayOfWeek.Sunday;
    }
}
