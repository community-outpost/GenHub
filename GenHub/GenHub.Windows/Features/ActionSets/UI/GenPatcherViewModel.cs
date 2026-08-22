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
using GenHub.Core.Models.Results;
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
    /// Gets a value indicating whether the user can change the target installation (not busy).
    /// </summary>
    public bool CanChangeInstallation => !isBatchApplying && actionSets.All(x => !x.IsApplying);

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

    private static bool MatchesCategory(ActionSetViewModel vm, string category) =>
        string.IsNullOrEmpty(category) ||
        string.Equals(category, "All", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(vm.Category, category, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesStatus(ActionSetViewModel vm, string status)
    {
        if (string.IsNullOrEmpty(status) || string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return status switch
        {
            "Applied" => vm.IsApplied,
            "Not Applied" => vm.IsApplicable && !vm.IsApplied,
            "Not Applicable" => !vm.IsApplicable,
            _ => true,
        };
    }

    private static bool MatchesSearch(ActionSetViewModel vm, string query) =>
        string.IsNullOrEmpty(query) ||
        (!string.IsNullOrEmpty(vm.Title) && vm.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrEmpty(vm.Description) && vm.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrEmpty(vm.DetailedDescription) && vm.DetailedDescription.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrEmpty(vm.Category) && vm.Category.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static int GetSortPriority(ActionSetViewModel vm)
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

    private bool CanExecuteCancelBatchApply() => isBatchApplying;

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
        if (value != null && CanChangeInstallation)
        {
            logger.LogInformation("Selected installation changed to: {InstallType} at {Path}", value.InstallationType, value.InstallationPath);
            _ = RefreshFixesForInstallationAsync(value);
        }
    }

    partial void OnIsBatchApplyingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChangeInstallation));
        foreach (var vm in ActionSets)
        {
            vm.IsBatchApplying = value;
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

            var result = await Task.Run(() => installationDetector.DetectInstallationsAsync(CancellationToken.None), CancellationToken.None);
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
        if (_refreshCts != null)
        {
            await _refreshCts.CancelAsync();
            _refreshCts.Dispose();
        }

        _refreshCts = new CancellationTokenSource();
        var ct = _refreshCts.Token;

        try
        {
            logger.LogInformation(
                "Using installation: {InstallType} at {Path} (refresh version {Version})",
                installation.InstallationType,
                installation.InstallationPath,
                version);

            var sortedVms = await LoadAndSortActionSetViewModelsAsync(installation, ct);

            if (ct.IsCancellationRequested || version != _refreshVersion || SelectedInstallation != installation)
            {
                logger.LogDebug("Refresh version {Version} was superseded or cancelled", version);
                return;
            }

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

            LogRefreshCompletionSummary(installation);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogDebug(ex, "Refresh fixes for installation {Path} was cancelled (version {Version})", installation.InstallationPath, version);
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

    private async Task<List<ActionSetViewModel>> LoadAndSortActionSetViewModelsAsync(GameInstallation installation, CancellationToken ct)
    {
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
                    () => IsBatchApplying || ActionSets.Any(x => !string.Equals(x.ActionSet.Id, fix.Id, StringComparison.OrdinalIgnoreCase) && x.IsApplying))
                {
                    IsBatchApplying = IsBatchApplying,
                };
                await vm.CheckStatusAsync(ct);
                return vm;
            },
            ct)).ToList();

        var loadedVms = await Task.WhenAll(tasks);

        return loadedVms
            .OrderBy(GetSortPriority)
            .ThenByDescending(vm => vm.IsCore)
            .ThenBy(vm => vm.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void LogRefreshCompletionSummary(GameInstallation installation)
    {
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

    private bool CanExecuteApplyAllFixes() => !isBatchApplying && selectedInstallation != null && actionSets.All(x => !x.IsApplying);

    private void NotifyExecutionStateChanged()
    {
        OnPropertyChanged(nameof(CanChangeInstallation));
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

        if (_batchCts != null)
        {
            await _batchCts.CancelAsync();
            _batchCts.Dispose();
        }

        _batchCts = new CancellationTokenSource();
        var ct = _batchCts.Token;

        IsBatchApplying = true;

        try
        {
            var applicableFixes = await GetApplicableCoreFixesAsync(targetInstallation, ct);
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

            await RefreshAllActionSetStatusesAsync();
            DisplayBatchResults(batchResult, targetInstallation, applicableFixes.Count, totalDuration);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Batch fix application was cancelled by user");
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
            _batchCts?.Dispose();
            _batchCts = null;
        }
    }

    private async Task<List<IActionSet>> GetApplicableCoreFixesAsync(GameInstallation targetInstallation, CancellationToken ct)
    {
        var coreFixes = await orchestrator.GetApplicableCoreFixesAsync(targetInstallation, ct);
        var coreFixIds = new HashSet<string>(coreFixes.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);

        return ActionSets
            .Where(vm => vm.IsApplicable && !vm.IsApplied && coreFixIds.Contains(vm.ActionSet.Id))
            .Select(vm => vm.ActionSet)
            .ToList();
    }

    private async Task RefreshAllActionSetStatusesAsync()
    {
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
    }

    private void DisplayBatchResults(
        OperationResult<int> batchResult,
        GameInstallation targetInstallation,
        int totalApplicable,
        double totalDuration)
    {
        int successCount = batchResult.Data;
        int errorCount = batchResult.Errors.Count;
        int notAttemptedCount = Math.Max(0, totalApplicable - successCount - errorCount);

        if (batchResult.Success)
        {
            logger.LogInformation(
                "Batch complete in {Duration:F1}s - {Success}/{Total} successful for {InstallType}",
                totalDuration,
                successCount,
                totalApplicable,
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
                $"Fixes Completed with Errors ({successCount}/{totalApplicable} successful)",
                failureSummary);
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

        var filtered = ActionSets
            .Where(x => MatchesCategory(x, category) && MatchesStatus(x, status) && MatchesSearch(x, query))
            .ToList();

        FilteredActionSets.Clear();
        foreach (var item in filtered)
        {
            FilteredActionSets.Add(item);
        }

        UpdateMetrics();
    }

    private void UpdateMetrics()
    {
        totalFixesCount = actionSets.Count;
        applicableFixesCount = actionSets.Count(x => x.IsApplicable);
        appliedFixesCount = actionSets.Count(x => x.IsApplicable && x.IsApplied);
        unappliedFixesCount = actionSets.Count(x => x.IsApplicable && !x.IsApplied);

        progressPercentage = applicableFixesCount > 0
            ? (double)appliedFixesCount / applicableFixesCount * 100.0
            : 0.0;

        progressSummaryText = $"{appliedFixesCount} of {applicableFixesCount} applied";

        allCategoryCount = actionSets.Count;
        coreCategoryCount = actionSets.Count(x => string.Equals(x.Category, ActionSetConstants.Categories.CoreAndStability, StringComparison.OrdinalIgnoreCase));
        compatibilityCategoryCount = actionSets.Count(x => string.Equals(x.Category, ActionSetConstants.Categories.Compatibility, StringComparison.OrdinalIgnoreCase));
        multiplayerCategoryCount = actionSets.Count(x => string.Equals(x.Category, ActionSetConstants.Categories.Multiplayer, StringComparison.OrdinalIgnoreCase));
        qolCategoryCount = actionSets.Count(x => string.Equals(x.Category, ActionSetConstants.Categories.QualityOfLife, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(TotalFixesCount));
        OnPropertyChanged(nameof(ApplicableFixesCount));
        OnPropertyChanged(nameof(AppliedFixesCount));
        OnPropertyChanged(nameof(UnappliedFixesCount));
        OnPropertyChanged(nameof(ProgressPercentage));
        OnPropertyChanged(nameof(ProgressSummaryText));
        OnPropertyChanged(nameof(AllCategoryCount));
        OnPropertyChanged(nameof(CoreCategoryCount));
        OnPropertyChanged(nameof(CompatibilityCategoryCount));
        OnPropertyChanged(nameof(MultiplayerCategoryCount));
        OnPropertyChanged(nameof(QolCategoryCount));
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
