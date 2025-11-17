using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameClients;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GameClients;

/// <summary>
/// Orchestrates installation detection and client detection.
/// </summary>
/// <param name="installationOrchestrator">The installation orchestrator.</param>
/// <param name="clientDetector">The client detector.</param>
/// <param name="logger">The logger instance.</param>
public sealed class GameClientDetectionOrchestrator(
    IGameInstallationDetectionOrchestrator installationOrchestrator,
    IGameClientDetector clientDetector,
    ILogger<GameClientDetectionOrchestrator> logger)
    : IGameClientDetectionOrchestrator
{
    private readonly IGameClientDetector _clientDetector = clientDetector;

    /// <inheritdoc/>
    public async Task<DetectionResult<GameClient>> DetectAllClientsAsync(
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting comprehensive game client detection");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await installationOrchestrator.DetectAllInstallationsAsync(cancellationToken);
            if (!result.Success)
            {
                logger.LogWarning("Installation detection failed: {Errors}", string.Join(", ", result.Errors));
                return DetectionResult<GameClient>.CreateFailure(string.Join(", ", result.Errors));
            }

            logger.LogDebug("Found {InstallationCount} installations, detecting clients", result.Items.Count);

            var allClients = new List<GameClient>();
            var errors = new List<string>();

            var clientResult = await _clientDetector.DetectGameClientsFromInstallationsAsync(result.Items, cancellationToken);
            if (clientResult.Success)
            {
                allClients.AddRange(clientResult.Items);
                logger.LogInformation("Successfully detected {ClientCount} game clients", clientResult.Items.Count);
            }
            else
            {
                errors.AddRange(clientResult.Errors);
                logger.LogWarning("Client detection failed: {Errors}", string.Join(", ", clientResult.Errors));
            }

            stopwatch.Stop();
            var finalResult = errors.Any()
                ? DetectionResult<GameClient>.CreateFailure(string.Join(", ", errors))
                : DetectionResult<GameClient>.CreateSuccess(allClients, stopwatch.Elapsed);

            logger.LogInformation(
                "Game client detection completed in {ElapsedMs}ms with {ClientCount} clients found",
                stopwatch.ElapsedMilliseconds,
                allClients.Count);

            return finalResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Game client detection failed with exception after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<DetectionResult<GameClient>> DetectGameClientsFromInstallationsAsync(
        IEnumerable<IGameInstallation> installations,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Detecting game clients from {InstallationCount} pre-detected installations", installations.Count());
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var allGameClients = new List<GameClient>();
            var errors = new List<string>();

            // Cast to concrete type as required by detector
            var concreteInstallations = installations.Cast<Core.Models.GameInstallations.GameInstallation>();
            var clientResult = await _clientDetector.DetectGameClientsFromInstallationsAsync(concreteInstallations, cancellationToken);
            if (clientResult.Success)
            {
                allGameClients.AddRange(clientResult.Items);
                logger.LogInformation("Successfully detected {ClientCount} game clients from installations", clientResult.Items.Count);
            }
            else
            {
                errors.AddRange(clientResult.Errors);
                logger.LogWarning("Client detection from installations failed: {Errors}", string.Join(", ", clientResult.Errors));
            }

            stopwatch.Stop();
            var finalResult = errors.Any()
                ? DetectionResult<GameClient>.CreateFailure(string.Join(", ", errors))
                : DetectionResult<GameClient>.CreateSuccess(allGameClients, stopwatch.Elapsed);

            logger.LogInformation(
                "Client detection from installations completed in {ElapsedMs}ms with {ClientCount} clients found",
                stopwatch.ElapsedMilliseconds,
                allGameClients.Count);

            return finalResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Client detection from installations failed with exception after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<List<GameClient>> GetDetectedClientsAsync(
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting detected clients");
        var result = await DetectAllClientsAsync(cancellationToken);
        return result.Success ? result.Items.ToList() : new List<GameClient>();
    }
}