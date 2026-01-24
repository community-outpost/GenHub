namespace GenHub.Core.Features.ActionSets;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

/// <summary>
/// Implementation of the ActionSet orchestrator.
/// </summary>
public class ActionSetOrchestrator : IActionSetOrchestrator
{
    private readonly IEnumerable<IActionSet> _actionSets;
    private readonly ILogger<ActionSetOrchestrator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionSetOrchestrator"/> class.
    /// </summary>
    /// <param name="actionSets">The initial collection of action sets.</param>
    /// <param name="providers">The collection of action set providers.</param>
    /// <param name="logger">The logger instance.</param>
    public ActionSetOrchestrator(
        IEnumerable<IActionSet> actionSets,
        IEnumerable<IActionSetProvider> providers,
        ILogger<ActionSetOrchestrator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var allSets = new List<IActionSet>(actionSets ?? []);

        if (providers != null)
        {
            foreach (var provider in providers)
            {
                try
                {
                    allSets.AddRange(provider.GetActionSets());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load action sets from provider {Provider}", provider.GetType().Name);
                }
            }
        }

        _actionSets = allSets;
    }

    /// <inheritdoc/>
    public IEnumerable<IActionSet> GetAllActionSets() => _actionSets;

    /// <inheritdoc/>
    public async Task<IEnumerable<IActionSet>> GetApplicableCoreFixesAsync(GameInstallation installation)
    {
        var applicable = new List<IActionSet>();
        foreach (var actionSet in _actionSets.Where(x => x.IsCoreFix))
        {
            if (await actionSet.IsApplicableAsync(installation))
            {
                applicable.Add(actionSet);
            }
        }

        return applicable;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<int>> ApplyActionSetsAsync(
        GameInstallation installation,
        IEnumerable<IActionSet> actionSets,
        CancellationToken ct = default)
    {
        int successCount = 0;
        var errors = new List<string>();
        var actionSetsList = actionSets.ToList();
        int totalCount = actionSetsList.Count;

        _logger.LogInformation("Starting to apply {TotalCount} action sets to {Installation}", totalCount, installation.InstallationPath);

        for (int i = 0; i < actionSetsList.Count; i++)
        {
            var actionSet = actionSetsList[i];
            if (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Action set application cancelled by user");
                break;
            }

            // Double check applicability and applied state to avoid redundant work
            if (!await actionSet.IsApplicableAsync(installation))
            {
                _logger.LogDebug("Skipping {Title} - not applicable", actionSet.Title);
                continue;
            }

            if (await actionSet.IsAppliedAsync(installation))
            {
                _logger.LogDebug("Skipping {Title} - already applied", actionSet.Title);
                continue;
            }

            _logger.LogInformation("Applying fix {Current}/{Total}: {Title}", i + 1, totalCount, actionSet.Title);

            var result = await actionSet.ApplyAsync(installation, ct);
            if (result.Success)
            {
                successCount++;
                _logger.LogInformation("✓ Successfully applied {Title} ({Current}/{Total})", actionSet.Title, i + 1, totalCount);

                if (result.Details?.Count > 0)
                {
                    foreach (var detail in result.Details)
                    {
                        _logger.LogDebug("  {Detail}", detail);
                    }
                }
            }
            else
            {
                var errorMsg = $"Failed to apply {actionSet.Title}: {result.ErrorMessage}";
                errors.Add(errorMsg);
                _logger.LogWarning("✗ {ErrorMsg}", errorMsg);

                if (result.Details?.Count > 0)
                {
                    foreach (var detail in result.Details)
                    {
                        _logger.LogDebug("  {Detail}", detail);
                    }
                }

                if (actionSet.IsCrucialFix)
                {
                    _logger.LogError("Critical fix {Title} failed for {Installation}. Aborting sequence.", actionSet.Title, installation.InstallationPath);
                    errors.Add($"Critical fix '{actionSet.Title}' failed. Remaining fixes were not applied.");
                    return OperationResult<int>.CreateFailure(errors, successCount);
                }
            }
        }

        _logger.LogInformation(
            "Action set application completed: {SuccessCount}/{TotalCount} successful, {ErrorCount} errors",
            successCount,
            totalCount,
            errors.Count);

        if (errors.Count > 0)
        {
            return OperationResult<int>.CreateFailure(errors, successCount);
        }

        return OperationResult<int>.CreateSuccess(successCount);
    }
}
