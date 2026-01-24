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
public partial class ActionSetViewModel : ObservableObject
{
    /// <summary>
    /// Gets the underlying action set.
    /// </summary>
    public IActionSet ActionSet { get; }

    private readonly GameInstallation _installation;
    private readonly IRegistryService _registryService;
    private readonly INotificationService _notificationService;
    private readonly ILogger _logger;

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
    public string StatusDisplay => IsApplied ? "APPLIED" : (IsApplicable ? "NOT INSTALLED" : "NOT APPLICABLE");

    /// <summary>
    /// Gets the color for the status display.
    /// </summary>
    public string StatusColor => IsApplied ? "#44FF44" : (IsApplicable ? "#FFFFFF" : "#888888");

    /// <summary>
    /// Gets the background color for the status badge.
    /// </summary>
    public string StatusBackground => IsApplied ? "#2200FF00" : (IsApplicable ? "#22FFFFFF" : "#11FFFFFF");

    /// <summary>
    /// Gets the border color for the status badge.
    /// </summary>
    public string StatusBorder => IsApplied ? "#4400FF00" : (IsApplicable ? "#44FFFFFF" : "#22FFFFFF");

    [ObservableProperty]
    private AsyncRelayCommand _applyCommand;

    [ObservableProperty]
    private AsyncRelayCommand _forceApplyCommand;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionSetViewModel"/> class.
    /// </summary>
    /// <param name="actionSet">The action set.</param>
    /// <param name="installation">The game installation.</param>
    /// <param name="registryService">The registry service.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="logger">The logger instance.</param>
    public ActionSetViewModel(IActionSet actionSet, GameInstallation installation, IRegistryService registryService, INotificationService notificationService, ILogger logger)
    {
        ActionSet = actionSet;
        _installation = installation;
        _registryService = registryService;
        _notificationService = notificationService;
        _logger = logger;
        _applyCommand = new AsyncRelayCommand(ApplyAsync);
        _forceApplyCommand = new AsyncRelayCommand(ForceApplyAsync);

        _logger.LogDebug(
            "Created ActionSetViewModel for {Title} (ID={Id}, IsCore={IsCore})",
            actionSet.Title,
            actionSet.Id,
            actionSet.IsCoreFix);
    }

    /// <summary>
    /// Checks the status of the action set (applicable and applied).
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CheckStatusAsync()
    {
        try
        {
            _logger.LogInformation(
                "[GENPATCHER_CHECK_005] Checking status for {Title} (ID={Id})",
                ActionSet.Title,
                ActionSet.Id);

            IsApplicable = await ActionSet.IsApplicableAsync(_installation);
            IsApplied = await ActionSet.IsAppliedAsync(_installation);

            _logger.LogInformation(
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
            _logger.LogError(
                ex,
                "[GENPATCHER_CHECK_006] Failed to check status for {Title} (ID={Id})",
                ActionSet.Title,
                ActionSet.Id);
            throw;
        }
    }

    private async Task ApplyAsync()
    {
        if (!_registryService.IsRunningAsAdministrator())
        {
            _logger.LogWarning(
                "[GENPATCHER_FIX_008] Cannot apply {Title} - not running as administrator",
                ActionSet.Title);
            _notificationService.ShowError(
                "Administrator Rights Required",
                "Please restart GenHub as Administrator to apply this fix.");
            return;
        }

        try
        {
            _logger.LogInformation(
                "[GENPATCHER_FIX_009] Starting application of {Title} (ID={Id}) to {InstallPath}",
                ActionSet.Title,
                ActionSet.Id,
                _installation.InstallationPath);

            var startTime = DateTime.UtcNow;
            var result = await ActionSet.ApplyAsync(_installation);
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            await CheckStatusAsync();

            if (result.Success)
            {
                var detailsText = result.Details.Count > 0
                    ? result.FormatDetails()
                    : $"{ActionSet.Title} has been successfully applied.";

                _logger.LogInformation(
                    "✓ {Title} applied successfully in {Duration}ms - {Details}",
                    ActionSet.Title,
                    (int)duration,
                    result.Details.Count > 0 ? string.Join("; ", result.Details) : "No details provided");

                _notificationService.ShowSuccess(
                    $"Fix Applied: {ActionSet.Title}",
                    detailsText);
            }
            else
            {
                var detailsText = result.Details.Count > 0
                    ? result.FormatDetails()
                    : result.ErrorMessage ?? "Unknown error occurred.";

                _logger.LogError(
                    "✗ [GENPATCHER_FIX_010] {Title} failed in {Duration}ms - {Error} - {Details}",
                    ActionSet.Title,
                    (int)duration,
                    result.ErrorMessage ?? "Unknown error",
                    result.Details.Count > 0 ? string.Join("; ", result.Details) : "No details");

                _notificationService.ShowError(
                    $"Fix Failed: {ActionSet.Title}",
                    detailsText);
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(
                ex,
                "[GENPATCHER_FIX_011] Exception applying {Title} (ID={Id})",
                ActionSet.Title,
                ActionSet.Id);
            _notificationService.ShowError(
                "Failed to Apply Fix",
                $"Could not apply {ActionSet.Title}: {ex.Message}");
        }
    }

    private async Task ForceApplyAsync()
    {
        if (!_registryService.IsRunningAsAdministrator())
        {
            _logger.LogWarning(
                "[GENPATCHER_FIX_012] Cannot force apply {Title} - not running as administrator",
                ActionSet.Title);
            _notificationService.ShowError(
                "Administrator Rights Required",
                "Please restart GenHub as Administrator for force apply.");
            return;
        }

        try
        {
            _logger.LogInformation(
                "[GENPATCHER_FIX_013] Starting FORCE application of {Title} (ID={Id}) to {InstallPath}",
                ActionSet.Title,
                ActionSet.Id,
                _installation.InstallationPath);

            var startTime = DateTime.UtcNow;
            var result = await ActionSet.ApplyAsync(_installation);
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            await CheckStatusAsync();

            if (result.Success)
            {
                var detailsText = result.Details.Count > 0
                    ? result.FormatDetails()
                    : $"{ActionSet.Title} has been force applied successfully.";

                _logger.LogInformation(
                    "✓ {Title} force applied successfully in {Duration}ms - {Details}",
                    ActionSet.Title,
                    (int)duration,
                    result.Details.Count > 0 ? string.Join("; ", result.Details) : "No details provided");

                _notificationService.ShowSuccess(
                    $"Fix Force Applied: {ActionSet.Title}",
                    detailsText);
            }
            else
            {
                var detailsText = result.Details.Count > 0
                    ? result.FormatDetails()
                    : result.ErrorMessage ?? "Unknown error occurred.";

                _logger.LogError(
                    "✗ [GENPATCHER_FIX_014] {Title} force apply failed in {Duration}ms - {Error} - {Details}",
                    ActionSet.Title,
                    (int)duration,
                    result.ErrorMessage ?? "Unknown error",
                    result.Details.Count > 0 ? string.Join("; ", result.Details) : "No details");

                _notificationService.ShowError(
                    $"Fix Failed: {ActionSet.Title}",
                    detailsText);
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogError(
                ex,
                "[GENPATCHER_FIX_015] Exception force applying {Title} (ID={Id})",
                ActionSet.Title,
                ActionSet.Id);
            _notificationService.ShowError(
                "Failed to Force Apply Fix",
                $"Could not apply {ActionSet.Title}: {ex.Message}");
        }
    }
}
