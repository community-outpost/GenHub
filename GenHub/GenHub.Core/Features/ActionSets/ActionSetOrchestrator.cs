namespace GenHub.Core.Features.ActionSets;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

/// <summary>
/// Implementation of the ActionSet orchestrator.
/// </summary>
/// <param name="actionSets">The initial collection of action sets.</param>
/// <param name="providers">The collection of action set providers.</param>
/// <param name="logger">The logger instance.</param>
public class ActionSetOrchestrator(
    IEnumerable<IActionSet> actionSets,
    IEnumerable<IActionSetProvider> providers,
    ILogger<ActionSetOrchestrator> logger) : IActionSetOrchestrator
{
    private enum ExecutionOutcome
    {
        Success,
        Skipped,
        FailedNonCritical,
        FailedCritical,
    }

    private readonly IReadOnlyList<IActionSet> _actionSets = InitializeActionSets(actionSets, providers, logger);

    /// <inheritdoc/>
    public IReadOnlyList<IActionSet> GetAllActionSets() => _actionSets;

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
                logger.LogError(ex, "Error checking applicability for {Title}", actionSet.Title);
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
        var stopwatch = Stopwatch.StartNew();
        int successCount = 0;
        var errors = new List<string>();
        var actionSetsList = actionSets.ToList();
        int totalCount = actionSetsList.Count;

        logger.LogInformation("Starting to apply {TotalCount} action sets to {Installation}", totalCount, installation.InstallationPath);

        for (int i = 0; i < actionSetsList.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var outcome = await ProcessActionSetAsync(
                actionSetsList[i],
                installation,
                i + 1,
                totalCount,
                errors,
                ct);

            if (outcome == ExecutionOutcome.Success)
            {
                successCount++;
            }
            else if (outcome == ExecutionOutcome.FailedCritical)
            {
                return OperationResult<int>.CreateFailure(errors, successCount, stopwatch.Elapsed);
            }
        }

        stopwatch.Stop();
        logger.LogInformation(
            "Finished applying action sets. Success: {SuccessCount}/{TotalCount}, Errors: {ErrorCount}",
            successCount,
            totalCount,
            errors.Count);

        if (errors.Count > 0)
        {
            return OperationResult<int>.CreateFailure(errors, successCount, stopwatch.Elapsed);
        }

        return OperationResult<int>.CreateSuccess(successCount, stopwatch.Elapsed);
    }

    private static IReadOnlyList<IActionSet> InitializeActionSets(
        IEnumerable<IActionSet> actionSets,
        IEnumerable<IActionSetProvider> providers,
        ILogger<ActionSetOrchestrator> logger)
    {
        var setMap = new Dictionary<string, IActionSet>(StringComparer.OrdinalIgnoreCase);

        if (actionSets != null)
        {
            RegisterDirectActionSets(actionSets, setMap, logger);
        }

        if (providers != null)
        {
            RegisterProviderActionSets(providers, setMap, logger);
        }

        return setMap.Values.ToList();
    }

    private static void RegisterDirectActionSets(
        IEnumerable<IActionSet> actionSets,
        Dictionary<string, IActionSet> setMap,
        ILogger<ActionSetOrchestrator> logger)
    {
        foreach (var set in actionSets)
        {
            if (set == null)
            {
                continue;
            }

            if (!setMap.TryAdd(set.Id, set))
            {
                logger.LogWarning("Duplicate action set ID {Id} ignored from direct registration", set.Id);
            }
        }
    }

    private static void RegisterProviderActionSets(
        IEnumerable<IActionSetProvider> providers,
        Dictionary<string, IActionSet> setMap,
        ILogger<ActionSetOrchestrator> logger)
    {
        foreach (var provider in providers)
        {
            try
            {
                foreach (var set in provider.GetActionSets())
                {
                    if (set == null)
                    {
                        continue;
                    }

                    if (!setMap.TryAdd(set.Id, set))
                    {
                        logger.LogWarning("Duplicate action set ID {Id} ignored from provider {Provider}", set.Id, provider.GetType().Name);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load action sets from provider {Provider}", provider.GetType().Name);
            }
        }
    }

    private async Task<ExecutionOutcome> ProcessActionSetAsync(
        IActionSet actionSet,
        GameInstallation installation,
        int index,
        int totalCount,
        List<string> errors,
        CancellationToken ct)
    {
        var eligible = await CheckEligibilityAsync(actionSet, installation, errors, ct);
        if (eligible != ExecutionOutcome.Success)
        {
            return eligible;
        }

        return await ApplySingleActionSetAsync(actionSet, installation, index, totalCount, errors, ct);
    }

    private async Task<ExecutionOutcome> CheckEligibilityAsync(
        IActionSet actionSet,
        GameInstallation installation,
        List<string> errors,
        CancellationToken ct)
    {
        try
        {
            if (!await actionSet.IsApplicableAsync(installation, ct))
            {
                logger.LogDebug("Skipping {Title} - not applicable", actionSet.Title);
                return ExecutionOutcome.Skipped;
            }

            if (await actionSet.IsAppliedAsync(installation, ct))
            {
                logger.LogDebug("Skipping {Title} - already applied", actionSet.Title);
                return ExecutionOutcome.Skipped;
            }

            return ExecutionOutcome.Success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error checking eligibility for {Title}", actionSet.Title);
            errors.Add($"Error checking {actionSet.Title}: {ex.Message}");
            if (actionSet.IsCrucialFix)
            {
                logger.LogError("Critical fix {Title} eligibility check failed. Aborting sequence.", actionSet.Title);
                errors.Add($"Critical fix '{actionSet.Title}' eligibility check failed. Remaining fixes were not applied.");
                return ExecutionOutcome.FailedCritical;
            }

            return ExecutionOutcome.FailedNonCritical;
        }
    }

    private async Task<ExecutionOutcome> ApplySingleActionSetAsync(
        IActionSet actionSet,
        GameInstallation installation,
        int index,
        int totalCount,
        List<string> errors,
        CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Applying action set {Index}/{Total}: {Title}", index, totalCount, actionSet.Title);
            var result = await actionSet.ApplyAsync(installation, ct);

            if (result.Success)
            {
                logger.LogInformation("Successfully applied {Title}", actionSet.Title);
                return ExecutionOutcome.Success;
            }

            var errorMessage = result.ErrorMessage ?? "Unknown error";
            logger.LogWarning("Failed to apply {Title}: {Error}", actionSet.Title, errorMessage);
            errors.Add($"{actionSet.Title}: {errorMessage}");

            if (actionSet.IsCrucialFix)
            {
                logger.LogError("Critical fix {Title} failed. Aborting remaining action sets.", actionSet.Title);
                errors.Add($"Critical fix '{actionSet.Title}' failed. Remaining fixes were not applied.");
                return ExecutionOutcome.FailedCritical;
            }

            return ExecutionOutcome.FailedNonCritical;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error applying {Title}", actionSet.Title);
            errors.Add($"{actionSet.Title}: {ex.Message}");

            if (actionSet.IsCrucialFix)
            {
                logger.LogError(ex, "Critical fix {Title} threw unexpected exception. Aborting remaining action sets.", actionSet.Title);
                errors.Add($"Critical fix '{actionSet.Title}' encountered an unexpected error. Remaining fixes were not applied.");
                return ExecutionOutcome.FailedCritical;
            }

            return ExecutionOutcome.FailedNonCritical;
        }
    }
}
