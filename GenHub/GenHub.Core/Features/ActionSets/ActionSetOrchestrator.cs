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

            var actionSet = actionSetsList[i];

            // Double check applicability and applied state with exception shielding
            bool isApplicable = false;
            try
            {
                isApplicable = await actionSet.IsApplicableAsync(installation, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error checking applicability for {Title}", actionSet.Title);
                errors.Add($"Error checking applicability for {actionSet.Title}: {ex.Message}");
                if (actionSet.IsCrucialFix)
                {
                    logger.LogError("Critical fix {Title} applicability check failed. Aborting sequence.", actionSet.Title);
                    errors.Add($"Critical fix '{actionSet.Title}' applicability check failed. Remaining fixes were not applied.");
                    return OperationResult<int>.CreateFailure(errors, successCount, stopwatch.Elapsed);
                }

                continue;
            }

            if (!isApplicable)
            {
                logger.LogDebug("Skipping {Title} - not applicable", actionSet.Title);
                continue;
            }

            bool isApplied = false;
            try
            {
                isApplied = await actionSet.IsAppliedAsync(installation, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error checking applied state for {Title}", actionSet.Title);
                errors.Add($"Error checking applied state for {actionSet.Title}: {ex.Message}");
                if (actionSet.IsCrucialFix)
                {
                    logger.LogError("Critical fix {Title} applied check failed. Aborting sequence.", actionSet.Title);
                    errors.Add($"Critical fix '{actionSet.Title}' applied check failed. Remaining fixes were not applied.");
                    return OperationResult<int>.CreateFailure(errors, successCount, stopwatch.Elapsed);
                }

                continue;
            }

            if (isApplied)
            {
                logger.LogDebug("Skipping {Title} - already applied", actionSet.Title);
                continue;
            }

            try
            {
                logger.LogInformation("Applying action set {Index}/{Total}: {Title}", i + 1, totalCount, actionSet.Title);
                var result = await actionSet.ApplyAsync(installation, ct);

                if (result.Success)
                {
                    successCount++;
                    logger.LogInformation("Successfully applied {Title}", actionSet.Title);
                }
                else
                {
                    var errorMessage = result.ErrorMessage ?? "Unknown error";
                    logger.LogWarning("Failed to apply {Title}: {Error}", actionSet.Title, errorMessage);
                    errors.Add($"{actionSet.Title}: {errorMessage}");

                    if (actionSet.IsCrucialFix)
                    {
                        logger.LogError("Critical fix {Title} failed. Aborting remaining action sets.", actionSet.Title);
                        errors.Add($"Critical fix '{actionSet.Title}' failed. Remaining fixes were not applied.");
                        return OperationResult<int>.CreateFailure(errors, successCount, stopwatch.Elapsed);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Action set execution cancelled during {Title}", actionSet.Title);
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
                    return OperationResult<int>.CreateFailure(errors, successCount, stopwatch.Elapsed);
                }
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
}
