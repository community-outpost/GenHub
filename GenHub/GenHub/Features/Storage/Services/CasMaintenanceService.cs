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
public class CasMaintenanceService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CasConfiguration _config;
    private readonly ILogger<CasMaintenanceService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CasMaintenanceService"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider for scoped services.</param>
    /// <param name="config">CAS configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public CasMaintenanceService(
        IServiceProvider serviceProvider,
        IOptions<CasConfiguration> config,
        ILogger<CasMaintenanceService> logger)
    {
        _serviceProvider = serviceProvider;
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.EnableAutomaticGarbageCollection)
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
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
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
            _logger.LogInformation("CAS garbage collection completed: {ObjectsDeleted} objects deleted, {BytesFreed:N0} bytes freed in {Duration}", gcResult.ObjectsDeleted, gcResult.BytesFreed, gcResult.Duration);
        }
        else
        {
            _logger.LogWarning("CAS garbage collection failed: {ErrorMessage}", gcResult.ErrorMessage);
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
