namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix for EA App registry keys which are often missing or incorrect.
/// </summary>
/// <param name="registryService">The registry service.</param>
/// <param name="logger">The logger instance.</param>
public class EAAppRegistryFix(IRegistryService registryService, ILogger<EAAppRegistryFix> logger) : BaseActionSet(logger)
{
    private sealed record GameRegistryConfig(
        string GameName,
        string? GamePath,
        string AppKeyPath,
        string ErgcKeyPath,
        int VersionDWord,
        string DefaultSerial);

    /// <inheritdoc/>
    public override string Id => "EAAppRegistryFix";

    /// <inheritdoc/>
    public override string Title => "EA App Registry Fix";

    /// <inheritdoc/>
    public override string Description => "Restores missing EA App installation paths, version DWORDs, and registry serial keys required for the game to start.";

    /// <inheritdoc/>
    public override string DetailedDescription => "The modern EA App client frequently fails to write standard legacy registry keys for Generals and Zero Hour, triggering misleading DirectX 8.1 or Technical Difficulties startup errors. This fix creates the official EA Games registry paths, registers accurate version DWORDs, and populates necessary serial key entries (ergc).";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // Strictly only for EA App or unknown types that we want to force-fix registry for.
        if (installation.InstallationType != GameInstallationType.EaApp && installation.InstallationType != GameInstallationType.Unknown)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        bool applied = IsGeneralsRegistryValid(installation) && IsZeroHourRegistryValid(installation);
        return Task.FromResult(applied);
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        // Check if running as administrator - required for HKEY_LOCAL_MACHINE writes
        if (!registryService.IsRunningAsAdministrator())
        {
            details.Add("✗ Administrator privileges required");
            details.Add("  Please restart GenHub as Administrator to apply registry fixes.");
            return Task.FromResult(new ActionSetResult(false, "Administrator privileges required to write to HKEY_LOCAL_MACHINE.", details));
        }

        try
        {
            details.Add("Starting EA App registry configuration...");
            var failedOperations = new List<string>();

            bool generalsSucceeded = !installation.HasGenerals || ConfigureGameRegistry(
                new GameRegistryConfig(
                    "Generals",
                    installation.GeneralsPath,
                    RegistryConstants.EAAppGeneralsKeyPath,
                    RegistryConstants.EAAppGeneralsErgcKeyPath,
                    RegistryConstants.GeneralsVersionDWord,
                    ActionSetConstants.Serials.DefaultEAAppGeneralsSerial),
                failedOperations,
                details);

            bool zeroHourSucceeded = !installation.HasZeroHour || ConfigureGameRegistry(
                new GameRegistryConfig(
                    "Zero Hour",
                    installation.ZeroHourPath,
                    RegistryConstants.EAAppZeroHourKeyPath,
                    RegistryConstants.EAAppZeroHourErgcKeyPath,
                    RegistryConstants.ZeroHourVersionDWord,
                    ActionSetConstants.Serials.DefaultEAAppZeroHourSerial),
                failedOperations,
                details);

            if (!generalsSucceeded || !zeroHourSucceeded)
            {
                var errorSummary = $"Failed to write the following registry keys: {string.Join(", ", failedOperations)}. Ensure you are running as administrator.";
                return Task.FromResult(new ActionSetResult(false, errorSummary, details));
            }

            details.Add("✓ EA App registry configuration completed successfully");
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying EA App registry fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            details.Add("Reverting EA App registry entries...");

            if (installation.HasGenerals)
            {
                registryService.DeleteValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.InstallPathValueName);
                registryService.DeleteValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.VersionValueName);
                details.Add($"✓ Removed EA App registry entries for Generals at {RegistryConstants.EAAppGeneralsKeyPath}");
            }

            if (installation.HasZeroHour)
            {
                registryService.DeleteValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.InstallPathValueName);
                registryService.DeleteValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.VersionValueName);
                details.Add($"✓ Removed EA App registry entries for Zero Hour at {RegistryConstants.EAAppZeroHourKeyPath}");
            }

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error undoing EA App registry fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    private bool ConfigureGameRegistry(
        GameRegistryConfig config,
        List<string> failedOperations,
        List<string> details)
    {
        if (string.IsNullOrEmpty(config.GamePath))
        {
            return true;
        }

        details.Add($"Configuring EA App registry for {config.GameName}: {config.GamePath}");
        bool succeeded = true;

        if (!registryService.SetStringValue(config.AppKeyPath, RegistryConstants.InstallPathValueName, config.GamePath))
        {
            succeeded = false;
            failedOperations.Add($"{config.AppKeyPath}\\{RegistryConstants.InstallPathValueName}");
            details.Add("  ✗ Failed to set InstallPath");
        }
        else
        {
            details.Add($"  ✓ InstallPath = {config.GamePath}");
        }

        if (!registryService.SetIntValue(config.AppKeyPath, RegistryConstants.VersionValueName, config.VersionDWord))
        {
            succeeded = false;
            failedOperations.Add($"{config.AppKeyPath}\\{RegistryConstants.VersionValueName}");
            details.Add("  ✗ Failed to set Version");
        }
        else
        {
            details.Add($"  ✓ Version = {config.VersionDWord}");
        }

        var existingSerial = registryService.GetStringValue(config.ErgcKeyPath, string.Empty);
        if (string.IsNullOrEmpty(existingSerial))
        {
            if (!registryService.SetStringValue(config.ErgcKeyPath, string.Empty, config.DefaultSerial))
            {
                succeeded = false;
                failedOperations.Add($"{config.ErgcKeyPath}\\(Default)");
                details.Add("  ✗ Failed to set serial key");
            }
            else
            {
                details.Add($"  ✓ Serial key created: {config.DefaultSerial}");
            }
        }
        else
        {
            details.Add("  ✓ Serial key already exists");
        }

        if (succeeded)
        {
            details.Add($"✓ {config.GameName} registry configuration completed");
        }

        return succeeded;
    }

    private bool IsGeneralsRegistryValid(GameInstallation installation)
    {
        if (!installation.HasGenerals)
        {
            return true;
        }

        return IsGameRegistryValid(
            installation.GeneralsPath,
            RegistryConstants.EAAppGeneralsKeyPath,
            RegistryConstants.EAAppGeneralsErgcKeyPath,
            RegistryConstants.GeneralsVersionDWord);
    }

    private bool IsZeroHourRegistryValid(GameInstallation installation)
    {
        if (!installation.HasZeroHour)
        {
            return true;
        }

        return IsGameRegistryValid(
            installation.ZeroHourPath,
            RegistryConstants.EAAppZeroHourKeyPath,
            RegistryConstants.EAAppZeroHourErgcKeyPath,
            RegistryConstants.ZeroHourVersionDWord);
    }

    private bool IsGameRegistryValid(string? gamePath, string appKeyPath, string ergcKeyPath, int expectedVersion)
    {
        var installPath = registryService.GetStringValue(appKeyPath, RegistryConstants.InstallPathValueName);
        var version = registryService.GetIntValue(appKeyPath, RegistryConstants.VersionValueName);
        var serial = registryService.GetStringValue(ergcKeyPath, string.Empty);

        return string.Equals(installPath, gamePath, StringComparison.OrdinalIgnoreCase) &&
               version == expectedVersion &&
               !string.IsNullOrEmpty(serial);
    }
}
