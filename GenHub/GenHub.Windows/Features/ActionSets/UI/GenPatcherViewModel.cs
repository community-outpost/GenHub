namespace GenHub.Windows.Features.ActionSets.UI;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using Microsoft.Extensions.Logging;

/// <summary>
/// ViewModel for the GenPatcher feature.
/// </summary>
public partial class GenPatcherViewModel(
    IActionSetOrchestrator orchestrator,
    IGameInstallationDetector installationDetector,
    IRegistryService registryService,
    INotificationService notificationService,
    ILogger<GenPatcherViewModel> logger) : ObservableObject
{
    private GameInstallation? currentInstallation;

    [ObservableProperty]
    private ObservableCollection<ActionSetViewModel> actionSets = [];

    /// <summary>
    /// Initializes the ViewModel asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        logger.LogInformation("[GENPATCHER_INIT_001] GenPatcher tool opened by user");

        var isAdmin = registryService.IsRunningAsAdministrator();
        var osVersion = Environment.OSVersion.VersionString;
        var dotnetVersion = Environment.Version.ToString();

        logger.LogInformation(
            "System Info - OS: {OsVersion}, .NET: {DotNetVersion}, Admin: {IsAdmin}",
            osVersion,
            dotnetVersion,
            isAdmin);

        if (!isAdmin)
        {
            logger.LogWarning("GenPatcher running without administrator privileges - some fixes may fail");
            notificationService.ShowWarning(
                "Administrator Rights Required",
                "Please restart GenHub as Administrator to ensure GenPatcher can apply registry-based fixes.");
        }

        await LoadFixesCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task LoadFixesAsync()
    {
        try
        {
            logger.LogInformation("[GENPATCHER_LOAD_002] Detecting game installations...");
            notificationService.ShowInfo(
                "Loading GenPatcher",
                "Detecting game installations and loading available fixes...");

            var result = await installationDetector.DetectInstallationsAsync();
            if (!result.Success)
            {
                var errorSummary = result.Errors.Count > 0 ? string.Join("; ", result.Errors) : "Installation detection failed.";
                logger.LogError("[GENPATCHER_LOAD_003] Failed to detect game installations: {Error}", errorSummary);
                notificationService.ShowError(
                    "Detection Failed",
                    $"Failed to detect game installations: {errorSummary}");
                return;
            }

            var detected = result.Items;

            logger.LogInformation("Found {Count} game installation(s)", detected.Count);
            foreach (var inst in detected)
            {
                logger.LogDebug(
                    "Installation: {InstallType} at {Path}",
                    inst.InstallationType,
                    inst.InstallationPath);
            }

            GameInstallation? preferred = null;
            foreach (var item in detected)
            {
                if (item.InstallationType != GameInstallationType.Unknown)
                {
                    preferred = item;
                    break;
                }
            }

            currentInstallation = preferred;

            if (currentInstallation == null)
            {
                logger.LogError("[GENPATCHER_LOAD_003] No valid game installation found for GenPatcher");
                notificationService.ShowError(
                    "No Game Installation Found",
                    "Please ensure Command & Conquer Generals or Zero Hour is installed.");
                return;
            }

            logger.LogInformation(
                "Using installation: {InstallType} at {Path}",
                currentInstallation.InstallationType,
                currentInstallation.InstallationPath);

            var fixes = orchestrator.GetAllActionSets();
            logger.LogInformation("Loading {Count} action sets...", fixes.Count);

            var installation = currentInstallation;

            // Parallelize status checks to prevent UI blocking
            var tasks = new List<Task<ActionSetViewModel>>();
            foreach (var fix in fixes)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var vm = new ActionSetViewModel(
                        fix,
                        installation,
                        registryService,
                        notificationService,
                        logger,
                        () => Avalonia.Threading.Dispatcher.UIThread.Post(SortActionSets));
                    await vm.CheckStatusAsync();
                    return vm;
                }));
            }

            var loadedVms = await Task.WhenAll(tasks);
            var sortedVms = loadedVms
                .OrderBy(GetSortPriority)
                .ThenByDescending(vm => vm.IsCore)
                .ThenBy(vm => vm.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ActionSets.Clear();
                foreach (var vm in sortedVms)
                {
                    ActionSets.Add(vm);
                    logger.LogInformation(
                        "[{Title}] ID={Id}, IsCore={IsCore}, Applicable={Applicable}, Applied={Applied}",
                        vm.ActionSet.Title,
                        vm.ActionSet.Id,
                        vm.IsCore,
                        vm.IsApplicable,
                        vm.IsApplied);
                }
            });

            var applicableCount = ActionSets.Count(x => x.IsApplicable);
            var appliedAndApplicableCount = ActionSets.Count(x => x.IsApplicable && x.IsApplied);
            var totalAppliedCount = ActionSets.Count(x => x.IsApplied);
            var notApplicableCount = ActionSets.Count(x => !x.IsApplicable);
            var coreCount = ActionSets.Count(x => x.IsCore);

            logger.LogInformation(
                "Load complete - Total: {Total}, Core: {Core}, Applicable: {Applicable}, Applied (Total): {AppliedTotal}, Applied (Applicable): {AppliedApplicable}, NotApplicable: {NotApplicable}",
                ActionSets.Count,
                coreCount,
                applicableCount,
                totalAppliedCount,
                appliedAndApplicableCount,
                notApplicableCount);

            notificationService.ShowSuccess(
                "GenPatcher Loaded",
                $"Successfully loaded {ActionSets.Count} fixes.\nApplied: {appliedAndApplicableCount} / {applicableCount} applicable fixes.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GENPATCHER_LOAD_004] Failed to load fixes");
            notificationService.ShowError(
                "Failed to Load Fixes",
                $"An error occurred while loading fixes: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ApplyAllFixesAsync()
    {
        try
        {
            if (currentInstallation == null)
            {
                logger.LogError("[GENPATCHER_APPLY_004] Cannot apply fixes - no installation selected");
                return;
            }

            if (!registryService.IsRunningAsAdministrator())
            {
                logger.LogWarning("[GENPATCHER_APPLY_005] Apply batch rejected - not running as administrator");
                notificationService.ShowError(
                    "Administrator Rights Required",
                    "Administrator privileges required for 'Apply Recommended'. Please restart GenHub as Administrator.");
                return;
            }

            var coreFixes = await orchestrator.GetApplicableCoreFixesAsync(currentInstallation);
            var coreFixIds = new HashSet<string>(coreFixes.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);

            var applicableFixes = new List<IActionSet>();
            foreach (var vm in ActionSets)
            {
                if (vm.IsApplicable && !vm.IsApplied && (vm.IsCore || coreFixIds.Contains(vm.ActionSet.Id)))
                {
                    applicableFixes.Add(vm.ActionSet);
                }
            }

            if (applicableFixes.Count == 0)
            {
                var alreadyApplied = ActionSets.Count(x => x.IsApplied);
                var totalSets = ActionSets.Count;

                logger.LogInformation("No fixes to apply - {Applied}/{Total} already applied", alreadyApplied, totalSets);
                notificationService.ShowInfo(
                    "No Fixes to Apply",
                    $"All {alreadyApplied}/{totalSets} applicable fixes are already applied.");
                return;
            }

            logger.LogInformation(
                "[GENPATCHER_APPLY_006] Starting batch application of {Count} fixes via orchestrator: {FixList}",
                applicableFixes.Count,
                string.Join(", ", applicableFixes.Select(f => f.Id)));

            notificationService.ShowInfo(
                "Applying Fixes",
                $"Applying {applicableFixes.Count} recommended fix(es)...");

            var startTime = DateTime.UtcNow;
            var batchResult = await orchestrator.ApplyActionSetsAsync(currentInstallation, applicableFixes);
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;

            // Refresh status
            logger.LogInformation("Refreshing fix status after batch application...");
            foreach (var vm in ActionSets)
            {
                try
                {
                    await vm.CheckStatusAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error refreshing status for {Title}", vm.ActionSet.Title);
                }
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(SortActionSets);

            int successCount = batchResult.Data;
            int failureCount = applicableFixes.Count - successCount;

            if (batchResult.Success)
            {
                logger.LogInformation(
                    "Batch complete in {Duration:F1}s - {Success}/{Total} successful",
                    totalDuration,
                    successCount,
                    applicableFixes.Count);

                notificationService.ShowSuccess(
                    "All Fixes Applied Successfully",
                    $"✓ Successfully applied all {successCount} fix(es).\n\nYour game installation has been optimized!");
            }
            else
            {
                var errorDetails = string.Join("\n", batchResult.Errors);
                logger.LogWarning("Batch completed with errors: {Errors}", errorDetails);
                notificationService.ShowError(
                    $"Fixes Completed with Errors ({successCount}/{applicableFixes.Count} successful)",
                    $"✓ Successfully applied: {successCount}\n✗ Failed: {failureCount}\n\nErrors:\n{errorDetails}");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error during batch fix application");
            notificationService.ShowError("Batch Apply Error", $"An error occurred: {ex.Message}");
        }
    }

    private int GetSortPriority(ActionSetViewModel vm)
    {
        // 0: NOT APPLIED (applicable and needs fix) -> top
        // 1: APPLIED (applicable and already fixed)
        // 2: NOT APPLICABLE (not applicable to this game installation)
        if (vm.IsApplicable && !vm.IsApplied)
        {
            return 0;
        }

        if (vm.IsApplicable && vm.IsApplied)
        {
            return 1;
        }

        return 2;
    }

    private void SortActionSets()
    {
        var sorted = ActionSets
            .OrderBy(GetSortPriority)
            .ThenByDescending(vm => vm.IsCore)
            .ThenBy(vm => vm.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var isDifferent = false;
        for (var i = 0; i < sorted.Count; i++)
        {
            if (!ReferenceEquals(ActionSets[i], sorted[i]))
            {
                isDifferent = true;
                break;
            }
        }

        if (isDifferent)
        {
            ActionSets.Clear();
            foreach (var vm in sorted)
            {
                ActionSets.Add(vm);
            }
        }
    }
}
