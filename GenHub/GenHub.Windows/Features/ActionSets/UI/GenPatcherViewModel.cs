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
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Windows.Features.ActionSets.UI;

#pragma warning disable S2325 // Methods/properties bound by Avalonia XAML or Command patterns must be instance members

/// <summary>
/// ViewModel for the GenPatcher feature.
/// </summary>
public partial class GenPatcherViewModel(
    IActionSetOrchestrator orchestrator,
    IGameInstallationDetector installationDetector,
    IRegistryService registryService,
    INotificationService notificationService,
    IDialogService dialogService,
    ILogger<GenPatcherViewModel> logger,
    ILocalizationService? localizationService = null) : ObservableObject
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

    /// <summary>
    /// Gets the localized header text for the All category chip.
    /// </summary>
    public string AllCategoryText => localizationService != null
        ? string.Format(localizationService.GetString("Tools.GenPatcher.Category.AllCount") ?? "All ({0})", AllCategoryCount)
        : $"All ({AllCategoryCount})";

    /// <summary>
    /// Gets the localized header text for the Core &amp; Stability category chip.
    /// </summary>
    public string CoreCategoryText => localizationService != null
        ? string.Format(localizationService.GetString("Tools.GenPatcher.Category.CoreCount") ?? "Core & Stability ({0})", CoreCategoryCount)
        : $"Core & Stability ({CoreCategoryCount})";

    /// <summary>
    /// Gets the localized header text for the Compatibility category chip.
    /// </summary>
    public string CompatibilityCategoryText => localizationService != null
        ? string.Format(localizationService.GetString("Tools.GenPatcher.Category.CompatibilityCount") ?? "Compatibility ({0})", CompatibilityCategoryCount)
        : $"Compatibility ({CompatibilityCategoryCount})";

    /// <summary>
    /// Gets the localized header text for the Multiplayer category chip.
    /// </summary>
    public string MultiplayerCategoryText => localizationService != null
        ? string.Format(localizationService.GetString("Tools.GenPatcher.Category.MultiplayerCount") ?? "Multiplayer ({0})", MultiplayerCategoryCount)
        : $"Multiplayer ({MultiplayerCategoryCount})";

    /// <summary>
    /// Gets the localized header text for the Quality of Life category chip.
    /// </summary>
    public string QolCategoryText => localizationService != null
        ? string.Format(localizationService.GetString("Tools.GenPatcher.Category.QolCount") ?? "Quality of Life ({0})", QolCategoryCount)
        : $"Quality of Life ({QolCategoryCount})";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyAllFixesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBatchApplyCommand))]
    private bool isBatchApplying;

    private CancellationTokenSource? _batchCts;
    private CancellationTokenSource? _refreshCts;
    private int _refreshVersion;
    private bool _isRevertingSelection;

    /// <summary>
    /// Handles updates when the active localization culture changes.
    /// </summary>
    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateMetrics();
            foreach (var actionSet in ActionSets)
            {
                actionSet.RefreshLocalizedStrings();
            }

            ApplyFilter();
        });
    }

    /// <summary>
    /// Gets a value indicating whether the user can change the target installation (not busy).
    /// </summary>
    public bool CanChangeInstallation => !IsBatchApplying && ActionSets.All(x => !x.IsApplying);

    /// <summary>
    /// Initializes the ViewModel asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        if (localizationService != null)
        {
            localizationService.PropertyChanged -= OnLocalizationChanged;
            localizationService.PropertyChanged += OnLocalizationChanged;
        }

        logger.LogInformation("[GENPATCHER_INIT_001] GenPatcher tool opened by user");

        var isAdmin = await Task.Run(() => registryService.IsRunningAsAdministrator(), CancellationToken.None);
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
            var adminTitle = localizationService?.GetString("Tools.GenPatcher.Notify.AdminRequired") ?? "Administrator Rights Required";
            var adminDesc = localizationService?.GetString("Tools.GenPatcher.Notify.AdminRequiredDesc") ?? "Please restart GenHub as Administrator to ensure GenPatcher can apply registry-based fixes.";
            notificationService.ShowWarning(adminTitle, adminDesc);
        }

        await LoadFixesCommand.ExecuteAsync(null);
    }

    private static bool MatchesCategory(ActionSetViewModel vm, string category) =>
        string.IsNullOrEmpty(category) ||
        string.Equals(category, "All", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(vm.RawCategory, category, StringComparison.OrdinalIgnoreCase);

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
            var cancelTitle = localizationService?.GetString("Tools.GenPatcher.Notify.CancellingTitle") ?? "Cancelling";
            var cancelDesc = localizationService?.GetString("Tools.GenPatcher.Notify.CancellingDesc") ?? "Cancelling batch application after the current fix completes...";
            notificationService.ShowWarning(cancelTitle, cancelDesc);
        }
    }

    partial void OnSelectedInstallationChanged(GameInstallation? oldValue, GameInstallation? newValue)
    {
        if (_isRevertingSelection)
        {
            return;
        }

        if (newValue == null)
        {
            return;
        }

        if (!CanChangeInstallation)
        {
            logger.LogWarning("Cannot switch installation while fix is applying. Reverting to previous installation.");
            if (oldValue != null)
            {
                _isRevertingSelection = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        SelectedInstallation = oldValue;
                    }
                    finally
                    {
                        _isRevertingSelection = false;
                    }
                });
            }

            return;
        }

        ApplyAllFixesCommand.NotifyCanExecuteChanged();
        logger.LogInformation("Selected installation changed to: {InstallType} at {Path}", newValue.InstallationType, newValue.InstallationPath);
        _ = RefreshFixesForInstallationAsync(newValue);
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
            var loadTitle = localizationService?.GetString("Tools.GenPatcher.Notify.LoadingTitle") ?? "Loading GenPatcher";
            var loadDesc = localizationService?.GetString("Tools.GenPatcher.Notify.LoadingDesc") ?? "Detecting game installations and loading available fixes...";
            notificationService.ShowInfo(loadTitle, loadDesc);

            var result = await Task.Run(() => installationDetector.DetectInstallationsAsync(CancellationToken.None), CancellationToken.None);
            if (!result.Success)
            {
                var errorSummary = result.Errors.Count > 0 ? string.Join("; ", result.Errors) : "Installation detection failed.";
                logger.LogError("[GENPATCHER_LOAD_003] Failed to detect game installations: {Error}", errorSummary);
                var detTitle = localizationService?.GetString("Tools.GenPatcher.Notify.DetectionFailedTitle") ?? "Detection Failed";
                var detDesc = localizationService != null
                    ? string.Format(localizationService.GetString("Tools.GenPatcher.Notify.DetectionFailedDesc") ?? "Failed to detect game installations: {0}", errorSummary)
                    : $"Failed to detect game installations: {errorSummary}";
                notificationService.ShowError(detTitle, detDesc);
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
                var noInstTitle = localizationService?.GetString("Tools.GenPatcher.Notify.NoInstallFoundTitle") ?? "No Game Installation Found";
                var noInstDesc = localizationService?.GetString("Tools.GenPatcher.Notify.NoInstallFoundDesc") ?? "Please ensure Command & Conquer Generals or Zero Hour is installed.";
                notificationService.ShowError(noInstTitle, noInstDesc);
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
            var loadErrTitle = localizationService?.GetString("Tools.GenPatcher.Notify.LoadFixesFailedTitle") ?? "Failed to Load Fixes";
            var loadErrDesc = localizationService != null
                ? string.Format(localizationService.GetString("Tools.GenPatcher.Notify.LoadFixesFailedDesc") ?? "An error occurred while loading fixes: {0}", ex.Message)
                : $"An error occurred while loading fixes: {ex.Message}";
            notificationService.ShowError(loadErrTitle, loadErrDesc);
        }
    }

    private async Task RefreshFixesForInstallationAsync(GameInstallation installation)
    {
        var version = Interlocked.Increment(ref _refreshVersion);
        var ct = await ResetRefreshCancellationTokenAsync();

        try
        {
            logger.LogInformation(
                "Using installation: {InstallType} at {Path} (refresh version {Version})",
                installation.InstallationType,
                installation.InstallationPath,
                version);

            var sortedVms = await LoadAndSortActionSetViewModelsAsync(installation, ct);

            if (!IsRefreshValid(version, installation, ct))
            {
                logger.LogDebug("Refresh version {Version} was superseded or cancelled", version);
                return;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => PopulateActionSets(sortedVms, version, installation, ct));

            if (!IsRefreshValid(version, installation, ct))
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
            HandleRefreshException(ex, installation, version, ct);
        }
    }

    private async Task<CancellationToken> ResetRefreshCancellationTokenAsync()
    {
        if (_refreshCts != null)
        {
            await _refreshCts.CancelAsync();
            _refreshCts.Dispose();
        }

        _refreshCts = new CancellationTokenSource();
        return _refreshCts.Token;
    }

    private bool IsRefreshValid(int version, GameInstallation installation, CancellationToken ct) =>
        !ct.IsCancellationRequested && version == _refreshVersion && SelectedInstallation == installation;

    private void PopulateActionSets(List<ActionSetViewModel> sortedVms, int version, GameInstallation installation, CancellationToken ct)
    {
        if (!IsRefreshValid(version, installation, ct))
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
    }

    private void HandleRefreshException(Exception ex, GameInstallation installation, int version, CancellationToken ct)
    {
        if (version == _refreshVersion && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Error refreshing fixes for installation {Path}", installation.InstallationPath);
            var refErrTitle = localizationService?.GetString("Tools.GenPatcher.Notify.LoadFixesFailedTitle") ?? "Failed to Load Fixes";
            var refErrDesc = localizationService != null
                ? string.Format(localizationService.GetString("Tools.GenPatcher.Notify.LoadFixesFailedDesc") ?? "An error occurred while loading fixes: {0}", ex.Message)
                : $"An error occurred while loading fixes: {ex.Message}";
            notificationService.ShowError(refErrTitle, refErrDesc);
        }
        else
        {
            logger.LogDebug(ex, "Superseded refresh encountered an exception for installation {Path}", installation.InstallationPath);
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
                    () => IsBatchApplying || ActionSets.Any(x => !string.Equals(x.ActionSet.Id, fix.Id, StringComparison.OrdinalIgnoreCase) && x.IsApplying),
                    localizationService: localizationService)
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

        var loadedTitle = localizationService?.GetString("Tools.GenPatcher.Notify.LoadedTitle") ?? "GenPatcher Loaded";
        var loadedDesc = localizationService != null
            ? string.Format(localizationService.GetString("Tools.GenPatcher.Notify.LoadedDesc") ?? "Successfully loaded {0} fixes for {1}.\nApplied: {2} / {3} applicable fixes.", ActionSets.Count, installation.InstallationType, appliedAndApplicableCount, applicableCount)
            : $"Successfully loaded {ActionSets.Count} fixes for {installation.InstallationType}.\nApplied: {appliedAndApplicableCount} / {applicableCount} applicable fixes.";
        notificationService.ShowSuccess(loadedTitle, loadedDesc);
    }

    private bool CanExecuteApplyAllFixes() => !IsBatchApplying && SelectedInstallation != null && ActionSets.All(x => !x.IsApplying);

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
            var noSelTitle = localizationService?.GetString("Tools.GenPatcher.Notify.NoInstallSelectedTitle") ?? "No Installation Selected";
            var noSelDesc = localizationService?.GetString("Tools.GenPatcher.Notify.NoInstallSelectedDesc") ?? "Please select a game installation before applying fixes.";
            notificationService.ShowError(noSelTitle, noSelDesc);
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

        var dialogTitle = localizationService?.GetString("Tools.GenPatcher.Dialog.ApplyAllTitle") ?? ActionSetConstants.Dialogs.ApplyAllConfirmationTitle;
        var dialogMessage = localizationService != null
            ? string.Format(localizationService.GetString("Tools.GenPatcher.Dialog.ApplyAllMessage") ?? "Are you sure you want to apply all recommended fixes for {0}?\n\nThis will modify game files and configuration settings at:\n{1}", targetInstallation.InstallationType, targetInstallation.InstallationPath)
            : $"Are you sure you want to apply all recommended fixes for {targetInstallation.InstallationType}?\n\nThis will modify game files and configuration settings at:\n{targetInstallation.InstallationPath}";
        var confirmText = localizationService?.GetString("Tools.GenPatcher.Dialog.ApplyAllConfirm") ?? ActionSetConstants.Dialogs.ApplyAllConfirmButtonText;
        var cancelText = localizationService?.GetString("Tools.GenPatcher.Dialog.ApplyAllCancel") ?? ActionSetConstants.Dialogs.ApplyAllCancelButtonText;

        var confirmed = await dialogService.ShowConfirmationAsync(
            dialogTitle,
            dialogMessage,
            confirmText: confirmText,
            cancelText: cancelText);

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
                var noFixesTitle = localizationService?.GetString("Tools.GenPatcher.Notify.NoFixesToApplyTitle") ?? "No Fixes to Apply";
                var noFixesDesc = localizationService != null
                    ? string.Format(localizationService.GetString("Tools.GenPatcher.Notify.NoFixesToApplyDesc") ?? "All {0}/{1} applicable fixes are already applied for {2}.", alreadyApplied, totalSets, targetInstallation.InstallationType)
                    : $"All {alreadyApplied}/{totalSets} applicable fixes are already applied for {targetInstallation.InstallationType}.";
                notificationService.ShowInfo(noFixesTitle, noFixesDesc);
                return;
            }

            logger.LogInformation(
                "[GENPATCHER_APPLY_006] Starting batch application of {Count} fixes for {InstallType} ({Path}) via orchestrator: {FixList}",
                applicableFixes.Count,
                targetInstallation.InstallationType,
                targetInstallation.InstallationPath,
                string.Join(", ", applicableFixes.Select(f => f.Id)));

            var applyingTitle = localizationService?.GetString("Tools.GenPatcher.Notify.ApplyingFixesTitle") ?? "Applying Fixes";
            var applyingDesc = localizationService != null
                ? string.Format(localizationService.GetString("Tools.GenPatcher.Notify.ApplyingFixesDesc") ?? "Applying {0} recommended fix(es) to {1} ({2})...", applicableFixes.Count, targetInstallation.InstallationType, targetInstallation.InstallationPath)
                : $"Applying {applicableFixes.Count} recommended fix(es) to {targetInstallation.InstallationType} ({targetInstallation.InstallationPath})...";
            notificationService.ShowInfo(applyingTitle, applyingDesc);

            var startTime = DateTime.UtcNow;
            var batchResult = await orchestrator.ApplyActionSetsAsync(targetInstallation, applicableFixes, ct);
            var totalDuration = (DateTime.UtcNow - startTime).TotalSeconds;

            await RefreshAllActionSetStatusesAsync();
            DisplayBatchResults(batchResult, targetInstallation, applicableFixes.Count, totalDuration);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Batch fix application was cancelled by user");
            var batchCancelledTitle = localizationService?.GetString("Tools.GenPatcher.Notify.BatchCancelledTitle") ?? "Batch Cancelled";
            var batchCancelledDesc = localizationService?.GetString("Tools.GenPatcher.Notify.BatchCancelledDesc") ?? "Batch fix application was cancelled.";
            notificationService.ShowWarning(batchCancelledTitle, batchCancelledDesc);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error during batch fix application");
            var batchErrorTitle = localizationService?.GetString("Tools.GenPatcher.Notify.BatchApplyErrorTitle") ?? "Batch Apply Error";
            var batchErrorDesc = localizationService != null
                ? string.Format(localizationService.GetString("Tools.GenPatcher.Notify.BatchApplyErrorDesc") ?? "An error occurred: {0}", ex.Message)
                : $"An error occurred: {ex.Message}";
            notificationService.ShowError(batchErrorTitle, batchErrorDesc);
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
        TotalFixesCount = ActionSets.Count;
        ApplicableFixesCount = ActionSets.Count(x => x.IsApplicable);
        AppliedFixesCount = ActionSets.Count(x => x.IsApplicable && x.IsApplied);
        UnappliedFixesCount = ActionSets.Count(x => x.IsApplicable && !x.IsApplied);

        ProgressPercentage = ApplicableFixesCount > 0
            ? (double)AppliedFixesCount / ApplicableFixesCount * 100.0
            : 0.0;

        ProgressSummaryText = localizationService != null
            ? string.Format(localizationService.GetString("Tools.GenPatcher.ProgressSummary") ?? "{0} of {1} applied", AppliedFixesCount, ApplicableFixesCount)
            : $"{AppliedFixesCount} of {ApplicableFixesCount} applied";

        AllCategoryCount = ActionSets.Count;
        CoreCategoryCount = ActionSets.Count(x => string.Equals(x.RawCategory, ActionSetConstants.Categories.CoreAndStability, StringComparison.OrdinalIgnoreCase));
        CompatibilityCategoryCount = ActionSets.Count(x => string.Equals(x.RawCategory, ActionSetConstants.Categories.Compatibility, StringComparison.OrdinalIgnoreCase));
        MultiplayerCategoryCount = ActionSets.Count(x => string.Equals(x.RawCategory, ActionSetConstants.Categories.Multiplayer, StringComparison.OrdinalIgnoreCase));
        QolCategoryCount = ActionSets.Count(x => string.Equals(x.RawCategory, ActionSetConstants.Categories.QualityOfLife, StringComparison.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(AllCategoryText));
        OnPropertyChanged(nameof(CoreCategoryText));
        OnPropertyChanged(nameof(CompatibilityCategoryText));
        OnPropertyChanged(nameof(MultiplayerCategoryText));
        OnPropertyChanged(nameof(QolCategoryText));
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
