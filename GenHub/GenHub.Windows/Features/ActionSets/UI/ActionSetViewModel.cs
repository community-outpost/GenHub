namespace GenHub.Windows.Features.ActionSets.UI;

using System;
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
    Action? onStatusChanged = null) : ObservableObject
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
    /// Gets the description of the action set.
    /// </summary>
    public string Description => ActionSet.Title;

    /// <summary>
    /// Gets a value indicating whether this is a core fix.
    /// </summary>
    public bool IsCore => ActionSet.IsCoreFix;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(StatusBackground))]
    [NotifyPropertyChangedFor(nameof(StatusBorder))]
    private bool isApplicable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(StatusDisplay))]
    [NotifyPropertyChangedFor(nameof(StatusColor))]
    [NotifyPropertyChangedFor(nameof(StatusBackground))]
    [NotifyPropertyChangedFor(nameof(StatusBorder))]
    private bool isApplied;

    /// <summary>
    /// Gets a value indicating whether the fix can be applied.
    /// </summary>
    public bool CanApply => IsApplicable && !IsApplied;

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
        (true, _) => "#2228A745",
        (false, true) => "#22FFC107",
        (false, false) => "#15FFFFFF",
    };

    /// <summary>
    /// Gets the border color for the status badge.
    /// </summary>
    public string StatusBorder => (IsApplied, IsApplicable) switch
    {
        (true, _) => "#4428A745",
        (false, true) => "#44FFC107",
        (false, false) => "#25FFFFFF",
    };

    /// <summary>
    /// Checks the status of the action set (applicable and applied).
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CheckStatusAsync()
    {
        try
        {
            logger.LogInformation(
                "[GENPATCHER_CHECK_005] Checking status for {Title} (ID={Id})",
                ActionSet.Title,
                ActionSet.Id);

            IsApplicable = await ActionSet.IsApplicableAsync(installation);
            IsApplied = await ActionSet.IsAppliedAsync(installation);

            logger.LogInformation(
                "Status check complete: {Title} - Applicable={Applicable}, Applied={Applied}",
                ActionSet.Title,
                IsApplicable,
                IsApplied);
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

    [RelayCommand]
    private Task ApplyAsync() => ExecuteApplyAsync(isForce: false);

    [RelayCommand]
    private Task ForceApplyAsync() => ExecuteApplyAsync(isForce: true);

    private async Task ExecuteApplyAsync(bool isForce)
    {
        try
        {
            logger.LogInformation(
                isForce ? "[GENPATCHER_FIX_013] Starting FORCE application of {Title} (ID={Id}) to {InstallPath}" : "[GENPATCHER_FIX_009] Starting application of {Title} (ID={Id}) to {InstallPath}",
                ActionSet.Title,
                ActionSet.Id,
                installation.InstallationPath);

            var startTime = DateTime.UtcNow;
            var result = await ActionSet.ApplyAsync(installation);
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
                await CheckStatusAsync();
                onStatusChanged?.Invoke();
            }
            catch (Exception statusEx)
            {
                logger.LogWarning(statusEx, "Error refreshing status after apply for {Title}", ActionSet.Title);
            }
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
    }
}
