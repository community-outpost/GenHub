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
            bool generalsSucceeded = true;
            bool zeroHourSucceeded = true;

            if (installation.HasGenerals)
            {
                details.Add($"Configuring EA App registry for Generals: {installation.GeneralsPath}");

                if (!registryService.SetStringValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.InstallPathValueName, installation.GeneralsPath))
                {
                    generalsSucceeded = false;
                    failedOperations.Add($"{RegistryConstants.EAAppGeneralsKeyPath}\\{RegistryConstants.InstallPathValueName}");
                    details.Add("  ✗ Failed to set InstallPath");
                }
                else
                {
                    details.Add($"  ✓ InstallPath = {installation.GeneralsPath}");
                }

                if (!registryService.SetIntValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.VersionValueName, RegistryConstants.GeneralsVersionDWord))
                {
                    generalsSucceeded = false;
                    failedOperations.Add($"{RegistryConstants.EAAppGeneralsKeyPath}\\{RegistryConstants.VersionValueName}");
                    details.Add("  ✗ Failed to set Version");
                }
                else
                {
                    details.Add($"  ✓ Version = {RegistryConstants.GeneralsVersionDWord}");
                }

                var existingSerial = registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty);
                if (string.IsNullOrEmpty(existingSerial))
                {
                    var defaultSerial = ActionSetConstants.Serials.DefaultEAAppGeneralsSerial;
                    if (!registryService.SetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty, defaultSerial))
                    {
                        generalsSucceeded = false;
                        failedOperations.Add($"{RegistryConstants.EAAppGeneralsErgcKeyPath}\\(Default)");
                        details.Add("  ✗ Failed to set serial key");
                    }
                    else
                    {
                        details.Add($"  ✓ Serial key created: {defaultSerial}");
                    }
                }
                else
                {
                    details.Add("  ✓ Serial key already exists");
                }

                if (generalsSucceeded)
                {
                    details.Add("✓ Generals registry configuration completed");
                }
            }

            if (installation.HasZeroHour)
            {
                details.Add($"Configuring EA App registry for Zero Hour: {installation.ZeroHourPath}");

                if (!registryService.SetStringValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.InstallPathValueName, installation.ZeroHourPath))
                {
                    zeroHourSucceeded = false;
                    failedOperations.Add($"{RegistryConstants.EAAppZeroHourKeyPath}\\{RegistryConstants.InstallPathValueName}");
                    details.Add("  ✗ Failed to set InstallPath");
                }
                else
                {
                    details.Add($"  ✓ InstallPath = {installation.ZeroHourPath}");
                }

                if (!registryService.SetIntValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.VersionValueName, RegistryConstants.ZeroHourVersionDWord))
                {
                    zeroHourSucceeded = false;
                    failedOperations.Add($"{RegistryConstants.EAAppZeroHourKeyPath}\\{RegistryConstants.VersionValueName}");
                    details.Add("  ✗ Failed to set Version");
                }
                else
                {
                    details.Add($"  ✓ Version = {RegistryConstants.ZeroHourVersionDWord}");
                }

                var existingSerial = registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);
                if (string.IsNullOrEmpty(existingSerial))
                {
                    var defaultSerial = ActionSetConstants.Serials.DefaultEAAppZeroHourSerial;
                    if (!registryService.SetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty, defaultSerial))
                    {
                        zeroHourSucceeded = false;
                        failedOperations.Add($"{RegistryConstants.EAAppZeroHourErgcKeyPath}\\(Default)");
                        details.Add("  ✗ Failed to set serial key");
                    }
                    else
                    {
                        details.Add($"  ✓ Serial key created: {defaultSerial}");
                    }
                }
                else
                {
                    details.Add("  ✓ Serial key already exists");
                }

                if (zeroHourSucceeded)
                {
                    details.Add("✓ Zero Hour registry configuration completed");
                }
            }

            bool allSucceeded = generalsSucceeded && zeroHourSucceeded;
            if (!allSucceeded)
            {
                details.Add($"✗ Failed to write {failedOperations.Count} registry key(s)");
                foreach (var op in failedOperations)
                {
                    details.Add($"  • {op}");
                }

                return Task.FromResult(new ActionSetResult(false, $"Failed to write the following registry keys: {string.Join(", ", failedOperations)}. Ensure you are running as administrator.", details));
            }

            details.Add("✓ EA App registry configuration completed successfully");
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
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

    private bool IsGeneralsRegistryValid(GameInstallation installation)
    {
        if (!installation.HasGenerals)
        {
            return true;
        }

        var installPath = registryService.GetStringValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.InstallPathValueName);
        var version = registryService.GetIntValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.VersionValueName);
        var serial = registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty);

        return string.Equals(installPath, installation.GeneralsPath, StringComparison.OrdinalIgnoreCase) &&
               version == RegistryConstants.GeneralsVersionDWord &&
               !string.IsNullOrEmpty(serial);
    }

    private bool IsZeroHourRegistryValid(GameInstallation installation)
    {
        if (!installation.HasZeroHour)
        {
            return true;
        }

        var installPath = registryService.GetStringValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.InstallPathValueName);
        var version = registryService.GetIntValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.VersionValueName);
        var serial = registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);

        return string.Equals(installPath, installation.ZeroHourPath, StringComparison.OrdinalIgnoreCase) &&
               version == RegistryConstants.ZeroHourVersionDWord &&
               !string.IsNullOrEmpty(serial);
    }
}
