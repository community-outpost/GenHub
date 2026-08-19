namespace GenHub.Windows.Features.ActionSets.UI;

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

/// <summary>
/// View model for an individual action set.
/// </summary>
public partial class ActionSetViewModel(
    IActionSet actionSet,
    GameInstallation installation,
    IRegistryService registryService,
    INotificationService notificationService,
    ILogger logger) : ObservableObject
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
    public string Description => $"Fix ID: {ActionSet.Id}"; // Placeholder description

    /// <summary>
    /// Gets a value indicating whether this is a core fix.
    /// </summary>
    public bool IsCore => ActionSet.IsCoreFix;

    [ObservableProperty]
    private bool isApplicable;

    [ObservableProperty]
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
        (false, true) => "NOT INSTALLED",
        (false, false) => "NOT APPLICABLE",
    };

    /// <summary>
    /// Gets the color for the status display.
    /// </summary>
    public string StatusColor => (IsApplied, IsApplicable) switch
    {
        (true, _) => "#44FF44",
        (false, true) => "#FFFFFF",
        (false, false) => "#888888",
    };

    /// <summary>
    /// Gets the background color for the status badge.
    /// </summary>
    public string StatusBackground => (IsApplied, IsApplicable) switch
    {
        (true, _) => "#2200FF00",
        (false, true) => "#22FFFFFF",
        (false, false) => "#11FFFFFF",
    };

    /// <summary>
    /// Gets the border color for the status badge.
    /// </summary>
    public string StatusBorder => (IsApplied, IsApplicable) switch
    {
        (true, _) => "#4400FF00",
        (false, true) => "#44FFFFFF",
        (false, false) => "#22FFFFFF",
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

            // Notify dependent properties
            OnPropertyChanged(nameof(CanApply));
            OnPropertyChanged(nameof(StatusDisplay));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(StatusBackground));
            OnPropertyChanged(nameof(StatusBorder));
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
    private async Task ApplyAsync()
    {
        if (!registryService.IsRunningAsAdministrator())
        {
            logger.LogWarning(
                "[GENPATCHER_FIX_008] Cannot apply {Title} - not running as administrator",
                ActionSet.Title);
            notificationService.ShowError(
                "Administrator Rights Required",
                "Please restart GenHub as Administrator to apply this fix.");
            return;
        }

        try
        {
            logger.LogInformation(
                "[GENPATCHER_FIX_009] Starting application of {Title} (ID={Id}) to {InstallPath}",
                ActionSet.Title,
                ActionSet.Id,
                installation.InstallationPath);

            var startTime = DateTime.UtcNow;
            var result = await ActionSet.ApplyAsync(installation);
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            if (result.Success)
            {
                var detailsText = result.Details.Count > 0
                    ? result.FormatDetails()
                    : $"{ActionSet.Title} has been successfully applied.";

                logger.LogInformation(
                    "✓ {Title} applied successfully in {Duration}ms - {Details}",
                    ActionSet.Title,
                    (int)duration,
                    result.Details.Count > 0 ? string.Join("; ", result.Details) : "No details provided");

                notificationService.ShowSuccess(
                    $"Fix Applied: {ActionSet.Title}",
                    detailsText);
            }
            else
            {
                var detailsText = result.Details.Count > 0
                    ? result.FormatDetails()
                    : result.ErrorMessage ?? "Unknown error occurred.";

                logger.LogError(
                    "✗ [GENPATCHER_FIX_010] {Title} failed in {Duration}ms - {Error} - {Details}",
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
            }
            catch (Exception statusEx)
            {
                logger.LogWarning(statusEx, "Error refreshing status after fix application for {Title}", ActionSet.Title);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[GENPATCHER_FIX_011] Exception applying {Title} (ID={Id})",
                ActionSet.Title,
                ActionSet.Id);
            notificationService.ShowError(
                "Failed to Apply Fix",
                $"Could not apply {ActionSet.Title}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ForceApplyAsync()
    {
        if (!registryService.IsRunningAsAdministrator())
        {
            logger.LogWarning(
                "[GENPATCHER_FIX_012] Cannot force apply {Title} - not running as administrator",
                ActionSet.Title);
            notificationService.ShowError(
                "Administrator Rights Required",
                "Please restart GenHub as Administrator for force apply.");
            return;
        }

        try
        {
            logger.LogInformation(
                "[GENPATCHER_FIX_013] Starting FORCE application of {Title} (ID={Id}) to {InstallPath}",
                ActionSet.Title,
                ActionSet.Id,
                installation.InstallationPath);

            var startTime = DateTime.UtcNow;
            var result = await ActionSet.ApplyAsync(installation);
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            if (result.Success)
            {
                var detailsText = result.Details.Count > 0
                    ? result.FormatDetails()
                    : $"{ActionSet.Title} has been force applied successfully.";

                logger.LogInformation(
                    "✓ {Title} force applied successfully in {Duration}ms - {Details}",
                    ActionSet.Title,
                    (int)duration,
                    result.Details.Count > 0 ? string.Join("; ", result.Details) : "No details provided");

                notificationService.ShowSuccess(
                    $"Fix Force Applied: {ActionSet.Title}",
                    detailsText);
            }
            else
            {
                var detailsText = result.Details.Count > 0
                    ? result.FormatDetails()
                    : result.ErrorMessage ?? "Unknown error occurred.";

                logger.LogError(
                    "✗ [GENPATCHER_FIX_014] {Title} force apply failed in {Duration}ms - {Error} - {Details}",
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
            }
            catch (Exception statusEx)
            {
                logger.LogWarning(statusEx, "Error refreshing status after force apply for {Title}", ActionSet.Title);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[GENPATCHER_FIX_015] Exception force applying {Title} (ID={Id})",
                ActionSet.Title,
                ActionSet.Id);
            notificationService.ShowError(
                "Failed to Force Apply Fix",
                $"Could not apply {ActionSet.Title}: {ex.Message}");
        }
    }
}
