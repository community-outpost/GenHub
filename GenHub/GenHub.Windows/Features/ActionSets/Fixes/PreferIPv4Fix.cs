namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
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
    private readonly string _backupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GenHub",
        ActionSetConstants.Paths.SubActionSetMarkers,
        "PreferIPv4Fix.original");

    /// <inheritdoc/>
    public override string Id => "PreferIPv4Fix";

    /// <inheritdoc/>
    public override string Title => "Prefer IPv4";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            var currentValue = registryService.GetIntValue(
                RegistryConstants.Tcpip6ParametersKeyPath,
                RegistryConstants.DisabledComponentsValueName);

            var isApplied = currentValue == RegistryConstants.PreferIPv4DisabledComponentsValue;
            return Task.FromResult(isApplied);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking IPv4 preference status");
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

            var currentValue = registryService.GetIntValue(
                RegistryConstants.Tcpip6ParametersKeyPath,
                RegistryConstants.DisabledComponentsValueName);

            details.Add($"Current DisabledComponents value: {currentValue}");

            if (currentValue == RegistryConstants.PreferIPv4DisabledComponentsValue)
            {
                details.Add("✓ IPv4 preference is already enabled (IPv6 tunnels disabled)");
                logger.LogInformation("IPv4 preference is already enabled. No action needed.");
                return Task.FromResult(new ActionSetResult(true, null, details));
            }

            // Save original value to backup file before modifying
            try
            {
                var dir = Path.GetDirectoryName(_backupPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (!File.Exists(_backupPath))
                {
                    var backupValue = currentValue.HasValue ? currentValue.Value.ToString() : "absent";
                    File.WriteAllText(_backupPath, backupValue);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not save original DisabledComponents value to backup file");
                details.Add("✗ Could not back up the current IPv6 configuration");
                return Task.FromResult(new ActionSetResult(
                    false,
                    "Could not back up the current IPv6 configuration.",
                    details));
            }

            details.Add("Configuring system to prefer IPv4...");
            details.Add($"Registry: HKLM\\{RegistryConstants.Tcpip6ParametersKeyPath}");
            details.Add($"Key: {RegistryConstants.DisabledComponentsValueName}");
            details.Add($"New value: {RegistryConstants.PreferIPv4DisabledComponentsValue} (0x20 - Disable IPv6 tunnel interfaces)");

            logger.LogInformation("Enabling IPv4 preference by disabling IPv6 tunnel interfaces...");

            var writeSuccess = registryService.SetIntValue(
                RegistryConstants.Tcpip6ParametersKeyPath,
                RegistryConstants.DisabledComponentsValueName,
                RegistryConstants.PreferIPv4DisabledComponentsValue);

            if (!writeSuccess)
            {
                details.Add("✗ Failed to set DisabledComponents registry key (permissions?)");
                return Task.FromResult(new ActionSetResult(false, "Failed to write DisabledComponents registry key", details));
            }

            details.Add("✓ IPv4 preference enabled successfully");
            details.Add("⚠ IMPORTANT: Computer restart required for changes to take effect");
            details.Add("  After restart, IPv4 will be preferred for all network connections");

            logger.LogInformation("IPv4 preference fix applied with {Count} actions", details.Count);
            logger.LogInformation("NOTE: You may need to restart your computer for this change to take effect.");

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying IPv4 preference fix");
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

            var currentValue = registryService.GetIntValue(
                RegistryConstants.Tcpip6ParametersKeyPath,
                RegistryConstants.DisabledComponentsValueName);

            if (currentValue == null || currentValue == 0)
            {
                details.Add("✓ IPv4 preference is not set. No undo action needed.");
                logger.LogInformation("IPv4 preference is not set. No undo action needed.");
                return Task.FromResult(new ActionSetResult(true, null, details));
            }

            logger.LogInformation("Restoring original IPv4/IPv6 configuration...");

            bool restoreSuccess = false;
            if (File.Exists(_backupPath))
            {
                var savedVal = File.ReadAllText(_backupPath).Trim();
                if (savedVal.Equals("absent", StringComparison.OrdinalIgnoreCase))
                {
                    restoreSuccess = registryService.DeleteValue(
                        RegistryConstants.Tcpip6ParametersKeyPath,
                        RegistryConstants.DisabledComponentsValueName);
                }
                else if (int.TryParse(savedVal, out var origInt))
                {
                    restoreSuccess = registryService.SetIntValue(
                        RegistryConstants.Tcpip6ParametersKeyPath,
                        RegistryConstants.DisabledComponentsValueName,
                        origInt);
                }

                try
                {
                    File.Delete(_backupPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to clean up backup file");
                }
            }
            else
            {
                restoreSuccess = registryService.SetIntValue(
                    RegistryConstants.Tcpip6ParametersKeyPath,
                    RegistryConstants.DisabledComponentsValueName,
                    0);
            }

            if (!restoreSuccess)
            {
                details.Add("✗ Failed to reset DisabledComponents registry key");
                return Task.FromResult(new ActionSetResult(false, "Failed to reset DisabledComponents registry key", details));
            }

            details.Add("✓ IPv4 preference restored successfully");
            details.Add("⚠ Computer restart required for changes to take effect");

            logger.LogInformation("IPv4 preference removed successfully.");
            logger.LogInformation("NOTE: You may need to restart your computer for this change to take effect.");

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error undoing IPv4 preference fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }
}
