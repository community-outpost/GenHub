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

        var setMap = new Dictionary<string, IActionSet>(StringComparer.OrdinalIgnoreCase);

        if (actionSets != null)
        {
            foreach (var set in actionSets)
            {
                if (!setMap.TryAdd(set.Id, set))
                {
                    _logger.LogWarning("Duplicate action set ID {Id} ignored from direct registration", set.Id);
                }
            }
        }

        if (providers != null)
        {
            foreach (var provider in providers)
            {
                try
                {
                    foreach (var set in provider.GetActionSets())
                    {
                        if (!setMap.TryAdd(set.Id, set))
                        {
                            _logger.LogWarning("Duplicate action set ID {Id} ignored from provider {Provider}", set.Id, provider.GetType().Name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load action sets from provider {Provider}", provider.GetType().Name);
                }
            }
        }

        _actionSets = setMap.Values.ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<IActionSet> GetAllActionSets() => _actionSets.ToList();

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IActionSet>> GetApplicableCoreFixesAsync(GameInstallation installation, CancellationToken ct = default)
    {
        var applicable = new List<IActionSet>();
        foreach (var actionSet in _actionSets.Where(x => x.IsCoreFix))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await actionSet.IsApplicableAsync(installation, ct))
                {
                    applicable.Add(actionSet);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking applicability for {Title}", actionSet.Title);
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
                errors.Add($"Cancelled after {successCount} of {totalCount} fixes");
                return OperationResult<int>.CreateFailure(errors);
            }

            // Double check applicability and applied state with exception shielding
            bool isApplicable = false;
            try
            {
                isApplicable = await actionSet.IsApplicableAsync(installation, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Action set application cancelled by user");
                errors.Add($"Cancelled after {successCount} of {totalCount} fixes");
                return OperationResult<int>.CreateFailure(errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking applicability for {Title}", actionSet.Title);
                errors.Add($"Error checking applicability for {actionSet.Title}: {ex.Message}");
                if (actionSet.IsCrucialFix)
                {
                    _logger.LogError("Critical fix {Title} applicability check failed. Aborting sequence.", actionSet.Title);
                    errors.Add($"Critical fix '{actionSet.Title}' applicability check failed. Remaining fixes were not applied.");
                    return OperationResult<int>.CreateFailure(errors);
                }
                continue;
            }

            if (!isApplicable)
            {
                _logger.LogDebug("Skipping {Title} - not applicable", actionSet.Title);
                continue;
            }

            bool isApplied = false;
            try
            {
                isApplied = await actionSet.IsAppliedAsync(installation, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Action set application cancelled by user");
                errors.Add($"Cancelled after {successCount} of {totalCount} fixes");
                return OperationResult<int>.CreateFailure(errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking applied status for {Title}", actionSet.Title);
                errors.Add($"Error checking applied status for {actionSet.Title}: {ex.Message}");
                if (actionSet.IsCrucialFix)
                {
                    _logger.LogError("Critical fix {Title} applied check failed. Aborting sequence.", actionSet.Title);
                    errors.Add($"Critical fix '{actionSet.Title}' applied check failed. Remaining fixes were not applied.");
                    return OperationResult<int>.CreateFailure(errors);
                }
                isApplied = false;
            }

            if (isApplied)
            {
                _logger.LogDebug("Skipping {Title} - already applied", actionSet.Title);
                continue;
            }

            _logger.LogInformation("Applying fix {Current}/{Total}: {Title}", i + 1, totalCount, actionSet.Title);

            ActionSetResult result;
            try
            {
                result = await actionSet.ApplyAsync(installation, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Action set application cancelled by user");
                errors.Add($"Cancelled after {successCount} of {totalCount} fixes");
                return OperationResult<int>.CreateFailure(errors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error applying {Title}", actionSet.Title);
                result = new ActionSetResult(false, ex.Message);
            }

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
                    return OperationResult<int>.CreateFailure(errors);
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
            return OperationResult<int>.CreateFailure(errors);
        }

        return OperationResult<int>.CreateSuccess(successCount);
    }
}
