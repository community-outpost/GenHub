namespace GenHub.Windows.Features.ActionSets.UI;

using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// View model for an individual action set.
/// </summary>
public partial class ActionSetViewModel(
    IActionSet actionSet,
    GameInstallation installation,
    INotificationService notificationService,
    ILogger logger,
    Action? onStatusChanged = null,
    Action? onBusyChanged = null) : ObservableObject
{
    /// <summary>
    /// Gets the underlying action set.
    /// </summary>
    public IActionSet ActionSet { get; } = actionSet;

    /// <summary>
    /// Gets the title of the action set.
    /// </summary>
    public string Title => ActionSet.Title;

    /// <summary>
    /// Gets the concise description of the action set.
    /// </summary>
    public string Description => ActionSet.Description;

    /// <summary>
    /// Gets the detailed description of what the action set does.
    /// </summary>
    public string DetailedDescription => ActionSet.DetailedDescription;

    /// <summary>
    /// Gets the category of the action set.
    /// </summary>
    public string Category => ActionSet.Category;

    /// <summary>
    /// Gets a value indicating whether this is a core fix.
    /// </summary>
    public bool IsCore => ActionSet.IsCoreFix;

    /// <summary>
    /// Gets a value indicating whether this is a crucial fix for game stability.
    /// </summary>
    public bool IsCrucial => ActionSet.IsCrucialFix;

    /// <summary>
    /// Gets a value indicating whether this fix has a detailed description available.
    /// </summary>
    public bool HasDetailedDescription => !string.IsNullOrWhiteSpace(ActionSet.DetailedDescription);

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private string lastActionResultDetails = string.Empty;

    [ObservableProperty]
    private bool hasActionResultDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(StatusBackground))]
    [NotifyPropertyChangedFor(nameof(StatusBorder))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool isApplicable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(StatusBackground))]
    [NotifyPropertyChangedFor(nameof(StatusBorder))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool isApplied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForceApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelApplyCommand))]
    private bool isApplying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ForceApplyCommand))]
    private bool isBatchApplying;

    private CancellationTokenSource? _applyCts;

    /// <summary>
    /// Gets a value indicating whether the fix can be applied.
    /// </summary>
    public bool CanApply => IsApplicable && !IsApplied && !IsApplying && !IsBatchApplying;

    /// <summary>
    /// Gets the display status of the action set.
    /// </summary>
    public string StatusDisplay => (IsApplied, IsApplicable) switch
    {
        (true, _) => "APPLIED",
        (false, true) => "NOT APPLIED",
        (false, false) => "NOT APPLICABLE",
    };

    /// <summary>
    /// Gets the color for the status display.
    /// </summary>
    public string StatusColor => (IsApplied, IsApplicable) switch
    {
        (true, _) => ActionSetConstants.StatusColors.Applied,
        (false, true) => ActionSetConstants.StatusColors.Unapplied,
        (false, false) => ActionSetConstants.StatusColors.NotApplicable,
    };

    /// <summary>
    /// Gets the background color for the status badge.
    /// </summary>
    public string StatusBackground => (IsApplied, IsApplicable) switch
    {
        (true, _) => ActionSetConstants.StatusColors.AppliedBackground,
        (false, true) => ActionSetConstants.StatusColors.UnappliedBackground,
        (false, false) => ActionSetConstants.StatusColors.NotApplicableBackground,
    };

    /// <summary>
    /// Gets the border color for the status badge.
    /// </summary>
    public string StatusBorder => (IsApplied, IsApplicable) switch
    {
        (true, _) => ActionSetConstants.StatusColors.AppliedBorder,
        (false, true) => ActionSetConstants.StatusColors.UnappliedBorder,
        (false, false) => ActionSetConstants.StatusColors.NotApplicableBorder,
    };

    /// <summary>
    /// Checks the status of the action set (applicable and applied).
    /// </summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CheckStatusAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            logger.LogInformation(
                "[GENPATCHER_CHECK_005] Checking status for {Title} (ID={Id})",
                ActionSet.Title,
                ActionSet.Id);

            var applicable = await ActionSet.IsApplicableAsync(installation, ct);
            ct.ThrowIfCancellationRequested();

            var applied = await ActionSet.IsAppliedAsync(installation, ct);
            ct.ThrowIfCancellationRequested();

            IsApplicable = applicable;
            IsApplied = applied;

            logger.LogInformation(
                "Status check complete: {Title} - Applicable={Applicable}, Applied={Applied}",
                ActionSet.Title,
                IsApplicable,
                IsApplied);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[GENPATCHER_CHECK_006] Failed to check status for {Title} (ID={Id})",
                ActionSet.Title,
                ActionSet.Id);
        }
    }

    partial void OnIsApplyingChanged(bool value)
    {
        onBusyChanged?.Invoke();
    }

    private bool CanExecuteApply() => CanApply;

    private bool CanExecuteForceApply() => !IsApplying && !IsBatchApplying;

    private bool CanExecuteCancelApply() => IsApplying;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand(CanExecute = nameof(CanExecuteApply))]
    private Task ApplyAsync() => ExecuteApplyAsync(isForce: false);

    [RelayCommand(CanExecute = nameof(CanExecuteForceApply))]
    private Task ForceApplyAsync() => ExecuteApplyAsync(isForce: true);

    /// <summary>
    /// Cancels the ongoing individual fix application if running.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteCancelApply))]
    private void CancelApply()
    {
        if (_applyCts != null && !_applyCts.IsCancellationRequested)
        {
            logger.LogInformation("User cancelled application of {Title} (ID={Id})", ActionSet.Title, ActionSet.Id);
            _applyCts.Cancel();
            notificationService.ShowWarning("Cancelling", $"Cancelling application of {ActionSet.Title}...");
        }
    }

    private async Task ExecuteApplyAsync(bool isForce)
    {
        if (IsApplying || IsBatchApplying)
        {
            return;
        }

        _applyCts?.Cancel();
        _applyCts?.Dispose();
        _applyCts = new CancellationTokenSource();
        var ct = _applyCts.Token;

        try
        {
            IsApplying = true;
            CancelApplyCommand.NotifyCanExecuteChanged();

            logger.LogInformation(
                isForce ? "[GENPATCHER_FIX_013] Starting FORCE application of {Title} (ID={Id}) to {InstallPath}" : "[GENPATCHER_FIX_009] Starting application of {Title} (ID={Id}) to {InstallPath}",
                ActionSet.Title,
                ActionSet.Id,
                installation.InstallationPath);

            var startTime = DateTime.UtcNow;
            var result = await ActionSet.ApplyAsync(installation, ct);
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            if (result.Success)
            {
                string detailsText;
                if (result.Details.Count > 0)
                {
                    detailsText = result.FormatDetails();
                }
                else if (isForce)
                {
                    detailsText = $"{ActionSet.Title} has been force applied successfully.";
                }
                else
                {
                    detailsText = $"{ActionSet.Title} has been successfully applied.";
                }

                LastActionResultDetails = detailsText;
                HasActionResultDetails = true;

                logger.LogInformation(
                    isForce ? "✓ {Title} force applied successfully in {Duration}ms - {Details}" : "✓ {Title} applied successfully in {Duration}ms - {Details}",
                    ActionSet.Title,
                    (int)duration,
                    result.Details.Count > 0 ? string.Join("; ", result.Details) : "No details provided");

                notificationService.ShowSuccess(
                    isForce ? $"Fix Force Applied: {ActionSet.Title}" : $"Fix Applied: {ActionSet.Title}",
                    detailsText);
            }
            else
            {
                var detailsText = result.Details.Count > 0
                    ? result.FormatDetails()
                    : result.ErrorMessage ?? "Unknown error occurred.";

                LastActionResultDetails = detailsText;
                HasActionResultDetails = true;

                logger.LogError(
                    isForce ? "✗ [GENPATCHER_FIX_014] {Title} force apply failed in {Duration}ms - {Error} - {Details}" : "✗ [GENPATCHER_FIX_010] {Title} failed in {Duration}ms - {Error} - {Details}",
                    ActionSet.Title,
                    (int)duration,
                    result.ErrorMessage ?? "Unknown error",
                    result.Details.Count > 0 ? string.Join("; ", result.Details) : "No details");

                notificationService.ShowError(
                    $"Fix Failed: {ActionSet.Title}",
                    detailsText);
            }

            try
            {
                await CheckStatusAsync(CancellationToken.None);
                onStatusChanged?.Invoke();
            }
            catch (Exception statusEx)
            {
                logger.LogWarning(statusEx, "Error refreshing status after apply for {Title}", ActionSet.Title);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Application of {Title} was cancelled by user", ActionSet.Title);
            notificationService.ShowWarning("Apply Cancelled", $"Application of {ActionSet.Title} was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                isForce ? "[GENPATCHER_FIX_015] Exception force applying {Title} (ID={Id})" : "[GENPATCHER_FIX_011] Exception applying {Title} (ID={Id})",
                ActionSet.Title,
                ActionSet.Id);
            notificationService.ShowError(
                isForce ? "Failed to Force Apply Fix" : "Failed to Apply Fix",
                $"Could not apply {ActionSet.Title}: {ex.Message}");
        }
        finally
        {
            IsApplying = false;
            _applyCts?.Dispose();
            _applyCts = null;
            CancelApplyCommand.NotifyCanExecuteChanged();
        }
    }
}
