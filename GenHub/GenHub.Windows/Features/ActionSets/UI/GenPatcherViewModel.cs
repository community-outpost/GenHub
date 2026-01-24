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

            currentInstallation = preferred ?? (detected.Count > 0 ? detected[0] : null);

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
            logger.LogInformation("Loading {Count} action sets...", fixes.Count());
            ActionSets.Clear();

            var installation = currentInstallation;

            // Parallelize status checks to prevent UI blocking
            var tasks = new List<Task<ActionSetViewModel>>();
            foreach (var fix in fixes)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var vm = new ActionSetViewModel(fix, installation, registryService, notificationService, logger);
                    await vm.CheckStatusAsync();
                    return vm;
                }));
            }

            var loadedVms = await Task.WhenAll(tasks);
            foreach (var vm in loadedVms)
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

        var applicableFixes = new List<IActionSet>();
        foreach (var vm in ActionSets)
        {
            if (vm.IsApplicable && !vm.IsApplied)
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
            "[GENPATCHER_APPLY_006] Starting batch application of {Count} fixes: {FixList}",
            applicableFixes.Count,
            string.Join(", ", applicableFixes.Select(f => f.Id)));

        notificationService.ShowInfo(
            "Applying Fixes",
            $"Starting to apply {applicableFixes.Count} fix(es)...\nThis may take a few minutes.");

        // Apply fixes one by one with progress notifications
        int successCount = 0;
        var errors = new List<string>();
        var startTime = DateTime.UtcNow;

        for (int i = 0; i < applicableFixes.Count; i++)
        {
            var fix = applicableFixes[i];
            var fixNumber = i + 1;
            var total = applicableFixes.Count;

            // Show notification for current fix
            notificationService.ShowInfo(
                $"Applying Fix {fixNumber}/{total}",
                $"⚙ {fix.Title}");

            logger.LogInformation(
                "[{Current}/{Total}] Applying {Title} (ID={Id})",
                fixNumber,
                total,
                fix.Title,
                fix.Id);

            var fixStartTime = DateTime.UtcNow;

            // Apply the fix
            var fixResult = await fix.ApplyAsync(currentInstallation);

            var duration = (DateTime.UtcNow - fixStartTime).TotalMilliseconds;

            if (fixResult.Success)
            {
                successCount++;
                notificationService.ShowSuccess(
                    $"✓ Fix {fixNumber}/{total} Applied",
                    fix.Title);
                logger.LogInformation(
                    "✓ [{Title}] Success in {Duration}ms",
                    fix.Title,
                    (int)duration);
            }
            else
            {
                var errorMsg = $"{fix.Title}: {fixResult.ErrorMessage}";
                errors.Add(errorMsg);
                notificationService.ShowWarning(
                    $"✗ Fix {fixNumber}/{total} Failed",
                    $"{fix.Title}\n{fixResult.ErrorMessage}");
                logger.LogError(
                    "✗ [GENPATCHER_FIX_007] {Title} failed in {Duration}ms - {Error}",
                    fix.Title,
                    (int)duration,
                    fixResult.ErrorMessage);
            }
        }

        var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;

        // Refresh status
        logger.LogInformation("Refreshing fix status after batch application...");
        foreach (var vm in ActionSets)
        {
            await vm.CheckStatusAsync();
        }

        // Provide detailed summary
        var failureCount = applicableFixes.Count - successCount;

        logger.LogInformation(
            "Batch complete in {Duration}s - {Success}/{Total} successful, {Failed} failed",
            totalDuration,
            successCount,
            applicableFixes.Count,
            failureCount);

        if (errors.Count > 0)
        {
            var errorDetails = string.Join("\n\n", errors);

            logger.LogWarning("Batch completed with {Count} error(s): {Errors}", errors.Count, string.Join("; ", errors));
            notificationService.ShowError(
                $"Fixes Completed with Errors ({successCount}/{applicableFixes.Count} successful)",
                $"✓ Successfully applied: {successCount}\n✗ Failed: {failureCount}\n\nErrors:\n{errorDetails}");
        }
        else
        {
            notificationService.ShowSuccess(
                "All Fixes Applied Successfully",
                $"✓ Successfully applied all {applicableFixes.Count} fix(es).\n\nYour game installation has been optimized!");
        }
    }
}
