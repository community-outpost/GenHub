namespace GenHub.Windows.Features.ActionSets.UI;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.Common;
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
    IDialogService dialogService,
    ILogger<GenPatcherViewModel> logger) : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<GameInstallation> availableInstallations = [];

    [ObservableProperty]
    private GameInstallation? selectedInstallation;

    [ObservableProperty]
    private ObservableCollection<ActionSetViewModel> actionSets = [];

    [ObservableProperty]
    private ObservableCollection<ActionSetViewModel> filteredActionSets = [];

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string selectedCategory = "All";

    [ObservableProperty]
    private string selectedStatus = "All";

    [ObservableProperty]
    private int totalFixesCount;

    [ObservableProperty]
    private int applicableFixesCount;

    [ObservableProperty]
    private int appliedFixesCount;

    [ObservableProperty]
    private int unappliedFixesCount;

    [ObservableProperty]
    private double progressPercentage;

    [ObservableProperty]
    private string progressSummaryText = string.Empty;

    [ObservableProperty]
    private int allCategoryCount;

    [ObservableProperty]
    private int coreCategoryCount;

    [ObservableProperty]
    private int compatibilityCategoryCount;

    [ObservableProperty]
    private int multiplayerCategoryCount;

    [ObservableProperty]
    private int qolCategoryCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllFixesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBatchApplyCommand))]
    private bool isBatchApplying;

    private CancellationTokenSource? _batchCts;
    private CancellationTokenSource? _refreshCts;
    private int _refreshVersion;

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

    private bool CanExecuteCancelBatchApply() => IsBatchApplying;

    /// <summary>
    /// Cancels the ongoing batch fix application if running.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteCancelBatchApply))]
    private void CancelBatchApply()
    {
        if (_batchCts != null && !_batchCts.IsCancellationRequested)
        {
            logger.LogInformation("User cancelled batch fix application");
            _batchCts.Cancel();
            notificationService.ShowWarning("Cancelling", "Cancelling batch application after the current fix completes...");
        }
    }

    partial void OnSelectedInstallationChanged(GameInstallation? value)
    {
        ApplyAllFixesCommand.NotifyCanExecuteChanged();
        if (value != null && !IsBatchApplying)
        {
            logger.LogInformation("Selected installation changed to: {InstallType} at {Path}", value.InstallationType, value.InstallationPath);
            _ = RefreshFixesForInstallationAsync(value);
        }
    }

    partial void OnIsBatchApplyingChanged(bool value)
    {
        foreach (var vm in ActionSets)
        {
            vm.IsBatchApplying = value;
            vm.NotifyExecutionChanged();
        }
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
            var validInstallations = detected
                .Where(x => x.InstallationType != GameInstallationType.Unknown)
                .ToList();

            logger.LogInformation("Found {Count} valid game installation(s)", validInstallations.Count);
            foreach (var inst in validInstallations)
            {
                logger.LogDebug(
                    "Installation: {InstallType} at {Path}",
                    inst.InstallationType,
                    inst.InstallationPath);
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableInstallations.Clear();
                foreach (var inst in validInstallations)
                {
                    AvailableInstallations.Add(inst);
                }
            });

            if (validInstallations.Count == 0)
            {
                logger.LogError("[GENPATCHER_LOAD_003] No valid game installation found for GenPatcher");
                notificationService.ShowError(
                    "No Game Installation Found",
                    "Please ensure Command & Conquer Generals or Zero Hour is installed.");
                return;
            }

            if (SelectedInstallation == null || !validInstallations.Contains(SelectedInstallation))
            {
                SelectedInstallation = validInstallations[0];
            }
            else
            {
                await RefreshFixesForInstallationAsync(SelectedInstallation);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[GENPATCHER_LOAD_004] Failed to load fixes");
            notificationService.ShowError(
                "Failed to Load Fixes",
                $"An error occurred while loading fixes: {ex.Message}");
        }
    }

    private async Task RefreshFixesForInstallationAsync(GameInstallation installation)
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;

        try
        {
            logger.LogInformation(
                "Using installation: {InstallType} at {Path} (refresh version {Version})",
                installation.InstallationType,
                installation.InstallationPath,
                version);

            var fixes = orchestrator.GetAllActionSets();
            logger.LogInformation("Loading {Count} action sets...", fixes.Count);

            // Parallelize status checks to prevent UI blocking
            var tasks = fixes.Select(fix => Task.Run(
                async () =>
                {
                    ct.ThrowIfCancellationRequested();
                    var vm = new ActionSetViewModel(
                        fix,
                        installation,
                        notificationService,
                        logger,
                        () => Avalonia.Threading.Dispatcher.UIThread.Post(SortActionSets),
                        () => Avalonia.Threading.Dispatcher.UIThread.Post(NotifyExecutionStateChanged),
                        () => IsBatchApplying || ActionSets.Any(x => x.ActionSet.Id != fix.Id && x.IsApplying))
                    {
                        IsBatchApplying = IsBatchApplying,
                    };
                    await vm.CheckStatusAsync(ct);
                    return vm;
                },
                ct)).ToList();

            var loadedVms = await Task.WhenAll(tasks);

            if (ct.IsCancellationRequested || version != _refreshVersion || SelectedInstallation != installation)
            {
                logger.LogDebug("Refresh version {Version} was superseded or cancelled", version);
                return;
            }

            var sortedVms = loadedVms
                .OrderBy(GetSortPriority)
                .ThenByDescending(vm => vm.IsCore)
                .ThenBy(vm => vm.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested || version != _refreshVersion || SelectedInstallation != installation)
                {
                    return;
                }

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

                ApplyFilter();
                ApplyAllFixesCommand.NotifyCanExecuteChanged();
            });

            if (ct.IsCancellationRequested || version != _refreshVersion || SelectedInstallation != installation)
            {
                logger.LogDebug("Refresh version {Version} was superseded or cancelled", version);
                return;
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
                $"Successfully loaded {ActionSets.Count} fixes for {installation.InstallationType}.\nApplied: {appliedAndApplicableCount} / {applicableCount} applicable fixes.");
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Refresh fixes for installation {Path} was cancelled (version {Version})", installation.InstallationPath, version);
        }
        catch (Exception ex)
        {
            if (version == _refreshVersion && !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Error refreshing fixes for installation {Path}", installation.InstallationPath);
                notificationService.ShowError(
                    "Failed to Load Fixes",
                    $"An error occurred while loading fixes: {ex.Message}");
            }
            else
            {
                logger.LogDebug(ex, "Superseded refresh encountered an exception for installation {Path}", installation.InstallationPath);
            }
        }
    }

    private bool CanExecuteApplyAllFixes() => !IsBatchApplying && SelectedInstallation != null && !ActionSets.Any(x => x.IsApplying);

    private void NotifyExecutionStateChanged()
    {
        ApplyAllFixesCommand.NotifyCanExecuteChanged();
        foreach (var vm in ActionSets)
        {
            vm.NotifyExecutionChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteApplyAllFixes))]
    private async Task ApplyAllFixesAsync()
    {
        if (IsBatchApplying)
        {
            return;
        }

        if (SelectedInstallation == null)
        {
            logger.LogError("[GENPATCHER_APPLY_004] Cannot apply fixes - no installation selected");
            notificationService.ShowError("No Installation Selected", "Please select a game installation before applying fixes.");
            return;
        }

        var targetInstallation = SelectedInstallation;

        if (!registryService.IsRunningAsAdministrator())
        {
            logger.LogWarning("[GENPATCHER_APPLY_005] Apply batch rejected - not running as administrator");
            notificationService.ShowError(
                "Administrator Rights Required",
                "Administrator privileges required for 'Apply Recommended'. Please restart GenHub as Administrator.");
            return;
        }

        var confirmed = await dialogService.ShowConfirmationAsync(
            ActionSetConstants.Dialogs.ApplyAllConfirmationTitle,
            $"Are you sure you want to apply all recommended fixes for {targetInstallation.InstallationType}?\n\nThis will modify game files and configuration settings at:\n{targetInstallation.InstallationPath}",
            confirmText: ActionSetConstants.Dialogs.ApplyAllConfirmButtonText,
            cancelText: ActionSetConstants.Dialogs.ApplyAllCancelButtonText);

        if (!confirmed)
        {
            logger.LogInformation("Batch fix application cancelled by user at confirmation prompt");
            return;
        }

        _batchCts?.Cancel();
        _batchCts?.Dispose();
        _batchCts = new CancellationTokenSource();
        var ct = _batchCts.Token;

        IsBatchApplying = true;
        foreach (var vm in ActionSets)
        {
            vm.IsBatchApplying = true;
        }

        try
        {
            var coreFixes = await orchestrator.GetApplicableCoreFixesAsync(targetInstallation, ct);
            var coreFixIds = new HashSet<string>(coreFixes.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);

            var applicableFixes = new List<IActionSet>();
            foreach (var vm in ActionSets)
            {
                if (vm.IsApplicable && !vm.IsApplied && coreFixIds.Contains(vm.ActionSet.Id))
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
                    $"All {alreadyApplied}/{totalSets} applicable fixes are already applied for {targetInstallation.InstallationType}.");
                return;
            }

            logger.LogInformation(
                "[GENPATCHER_APPLY_006] Starting batch application of {Count} fixes for {InstallType} ({Path}) via orchestrator: {FixList}",
                applicableFixes.Count,
                targetInstallation.InstallationType,
                targetInstallation.InstallationPath,
                string.Join(", ", applicableFixes.Select(f => f.Id)));

            notificationService.ShowInfo(
                "Applying Fixes",
                $"Applying {applicableFixes.Count} recommended fix(es) to {targetInstallation.InstallationType} ({targetInstallation.InstallationPath})...");

            var startTime = DateTime.UtcNow;
            var batchResult = await orchestrator.ApplyActionSetsAsync(targetInstallation, applicableFixes, ct);
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;

            // Refresh status
            logger.LogInformation("Refreshing fix status after batch application...");
            foreach (var vm in ActionSets)
            {
                try
                {
                    await vm.CheckStatusAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error refreshing status for {Title}", vm.ActionSet.Title);
                }
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(SortActionSets);

            int successCount = batchResult.Data;
            int errorCount = batchResult.Errors.Count;
            int notAttemptedCount = Math.Max(0, applicableFixes.Count - successCount - errorCount);

            if (batchResult.Success)
            {
                logger.LogInformation(
                    "Batch complete in {Duration:F1}s - {Success}/{Total} successful for {InstallType}",
                    totalDuration,
                    successCount,
                    applicableFixes.Count,
                    targetInstallation.InstallationType);

                notificationService.ShowSuccess(
                    "All Fixes Applied Successfully",
                    $"✓ Successfully applied all {successCount} fix(es) to {targetInstallation.InstallationType} ({targetInstallation.InstallationPath}).\n\nYour game installation has been optimized!");
            }
            else
            {
                var errorDetails = string.Join("\n", batchResult.Errors);
                logger.LogWarning("Batch completed with errors: {Errors}", errorDetails);
                var failureSummary = notAttemptedCount > 0
                    ? $"Target: {targetInstallation.InstallationType} ({targetInstallation.InstallationPath})\n✓ Successfully applied: {successCount}\n✗ Failed: {errorCount}\n⚠ Not attempted: {notAttemptedCount}\n\nErrors:\n{errorDetails}"
                    : $"Target: {targetInstallation.InstallationType} ({targetInstallation.InstallationPath})\n✓ Successfully applied: {successCount}\n✗ Failed: {errorCount}\n\nErrors:\n{errorDetails}";

                notificationService.ShowError(
                    $"Fixes Completed with Errors ({successCount}/{applicableFixes.Count} successful)",
                    failureSummary);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Batch fix application was cancelled by user");
            notificationService.ShowWarning("Batch Cancelled", "Batch fix application was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error during batch fix application");
            notificationService.ShowError("Batch Apply Error", $"An error occurred: {ex.Message}");
        }
        finally
        {
            IsBatchApplying = false;
            foreach (var vm in ActionSets)
            {
                vm.IsBatchApplying = false;
            }

            _batchCts?.Dispose();
            _batchCts = null;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnSelectedCategoryChanged(string value) => ApplyFilter();

    partial void OnSelectedStatusChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void SetCategory(string category)
    {
        SelectedCategory = category;
    }

    [RelayCommand]
    private void SetStatusFilter(string status)
    {
        SelectedStatus = status;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    private void ApplyFilter()
    {
        var query = SearchQuery.Trim();
        var category = SelectedCategory;
        var status = SelectedStatus;

        var filtered = ActionSets.AsEnumerable();

        if (!string.IsNullOrEmpty(category) && !string.Equals(category, "All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(status) && !string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(status, "Applied", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(x => x.IsApplied);
            }
            else if (string.Equals(status, "Not Applied", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(x => x.IsApplicable && !x.IsApplied);
            }
            else if (string.Equals(status, "Not Applicable", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(x => !x.IsApplicable);
            }
        }

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(x =>
                (!string.IsNullOrEmpty(x.Title) && x.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.Description) && x.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.DetailedDescription) && x.DetailedDescription.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.Category) && x.Category.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var resultList = filtered.ToList();

        FilteredActionSets.Clear();
        foreach (var item in resultList)
        {
            FilteredActionSets.Add(item);
        }

        UpdateMetrics();
    }

    private void UpdateMetrics()
    {
        TotalFixesCount = ActionSets.Count;
        ApplicableFixesCount = ActionSets.Count(x => x.IsApplicable);
        AppliedFixesCount = ActionSets.Count(x => x.IsApplicable && x.IsApplied);
        UnappliedFixesCount = ActionSets.Count(x => x.IsApplicable && !x.IsApplied);

        ProgressPercentage = ApplicableFixesCount > 0
            ? (double)AppliedFixesCount / ApplicableFixesCount * 100.0
            : 0.0;

        ProgressSummaryText = $"{AppliedFixesCount} of {ApplicableFixesCount} applied";

        AllCategoryCount = ActionSets.Count;
        CoreCategoryCount = ActionSets.Count(x => string.Equals(x.Category, ActionSetConstants.Categories.CoreAndStability, StringComparison.OrdinalIgnoreCase));
        CompatibilityCategoryCount = ActionSets.Count(x => string.Equals(x.Category, ActionSetConstants.Categories.Compatibility, StringComparison.OrdinalIgnoreCase));
        MultiplayerCategoryCount = ActionSets.Count(x => string.Equals(x.Category, ActionSetConstants.Categories.Multiplayer, StringComparison.OrdinalIgnoreCase));
        QolCategoryCount = ActionSets.Count(x => string.Equals(x.Category, ActionSetConstants.Categories.QualityOfLife, StringComparison.OrdinalIgnoreCase));
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

        ApplyFilter();
    }
}
