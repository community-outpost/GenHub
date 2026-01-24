namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that disables IPv6 to prefer IPv4 for better multiplayer compatibility.
/// </summary>
public class PreferIPv4Fix(
    IRegistryService registryService,
    ILogger<PreferIPv4Fix> logger) : BaseActionSet(logger)
{
    private const string RegistryPath = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";
    private const string DisabledComponentsKey = "DisabledComponents";
    private const int PreferIPv4Value = 32; // Disable IPv6 tunnel interfaces

    private readonly IRegistryService _registryService = registryService;
    private readonly ILogger<PreferIPv4Fix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "PreferIPv4Fix";

    /// <inheritdoc/>
    public override string Title => "Prefer IPv4";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            var currentValue = _registryService.GetIntValue(
                RegistryPath,
                DisabledComponentsKey);

            var isApplied = currentValue == PreferIPv4Value;
            return Task.FromResult(isApplied);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking IPv4 preference status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            details.Add("Checking current IPv6 configuration...");

            var currentValue = _registryService.GetIntValue(
                RegistryPath,
                DisabledComponentsKey);

            details.Add($"Current DisabledComponents value: {currentValue}");

            if (currentValue == PreferIPv4Value)
            {
                details.Add("✓ IPv4 preference is already enabled (IPv6 tunnels disabled)");
                _logger.LogInformation("IPv4 preference is already enabled. No action needed.");
                return Task.FromResult(new ActionSetResult(true, null, details));
            }

            details.Add("Configuring system to prefer IPv4...");
            details.Add($"Registry: HKLM\\{RegistryPath}");
            details.Add($"Key: {DisabledComponentsKey}");
            details.Add($"New value: {PreferIPv4Value} (0x20 - Disable IPv6 tunnel interfaces)");

            _logger.LogInformation("Enabling IPv4 preference by disabling IPv6 tunnel interfaces...");

            _registryService.SetIntValue(
                RegistryPath,
                DisabledComponentsKey,
                PreferIPv4Value);

            details.Add("✓ IPv4 preference enabled successfully");
            details.Add("⚠ IMPORTANT: Computer restart required for changes to take effect");
            details.Add("  After restart, IPv4 will be preferred for all network connections");

            _logger.LogInformation("IPv4 preference fix applied with {Count} actions", details.Count);
            _logger.LogInformation("NOTE: You may need to restart your computer for this change to take effect.");

            return Task.FromResult(new ActionSetResult(true, "IPv4 preference enabled. Restart required.", details));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying IPv4 preference fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            details.Add("Removing IPv4 preference...");

            var currentValue = _registryService.GetStringValue(
                RegistryPath,
                DisabledComponentsKey);

            if (currentValue == null)
            {
                details.Add("✓ IPv4 preference is not set. No undo action needed.");
                _logger.LogInformation("IPv4 preference is not set. No undo action needed.");
                return Task.FromResult(new ActionSetResult(true, null, details));
            }

            _logger.LogInformation("Removing IPv4 preference...");

            _registryService.SetIntValue(
                RegistryPath,
                DisabledComponentsKey,
                0);

            details.Add("✓ IPv4 preference removed successfully");
            details.Add("⚠ Computer restart required for changes to take effect");

            _logger.LogInformation("IPv4 preference removed successfully.");
            _logger.LogInformation("NOTE: You may need to restart your computer for this change to take effect.");

            return Task.FromResult(new ActionSetResult(true, "IPv4 preference removed. Restart required.", details));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error undoing IPv4 preference fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }
}
