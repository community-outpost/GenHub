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
    private readonly IRegistryService _registryService = registryService ?? throw new ArgumentNullException(nameof(registryService));

    /// <inheritdoc/>
    public override string Id => "EAAppRegistryFix";

    /// <inheritdoc/>
    public override string Title => "EA App Registry Fix";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Strictly only for EA App or unknown types that we want to force-fix registry for.
        if (installation.InstallationType != GameInstallationType.EaApp && installation.InstallationType != GameInstallationType.Unknown)
        {
            return Task.FromResult(false);
        }

        // Applicable if keys are missing or point to wrong location
        bool fixNeeded = false;

        if (installation.HasGenerals)
        {
            var installPath = _registryService.GetStringValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.InstallPathValueName);
            var version = _registryService.GetIntValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.VersionValueName);
            var serial = _registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty); // Default value name is empty string

            if (!string.Equals(installPath, installation.GeneralsPath, StringComparison.OrdinalIgnoreCase) ||
                version != RegistryConstants.GeneralsVersionDWord ||
                string.IsNullOrEmpty(serial))
            {
                fixNeeded = true;
            }
        }

        if (installation.HasZeroHour)
        {
            var installPath = _registryService.GetStringValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.InstallPathValueName);
            var version = _registryService.GetIntValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.VersionValueName);
            var serial = _registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);

            if (!string.Equals(installPath, installation.ZeroHourPath, StringComparison.OrdinalIgnoreCase) ||
                version != RegistryConstants.ZeroHourVersionDWord ||
                string.IsNullOrEmpty(serial))
            {
                fixNeeded = true;
            }
        }

        return Task.FromResult(fixNeeded);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        if (installation.HasGenerals)
        {
            var installPath = _registryService.GetStringValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.InstallPathValueName);
            var version = _registryService.GetIntValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.VersionValueName);
            var serial = _registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty);

            if (!string.Equals(installPath, installation.GeneralsPath, StringComparison.OrdinalIgnoreCase) ||
                version != RegistryConstants.GeneralsVersionDWord ||
                string.IsNullOrEmpty(serial))
            {
                return Task.FromResult(false);
            }
        }

        if (installation.HasZeroHour)
        {
            var installPath = _registryService.GetStringValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.InstallPathValueName);
            var version = _registryService.GetIntValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.VersionValueName);
            var serial = _registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);

            if (!string.Equals(installPath, installation.ZeroHourPath, StringComparison.OrdinalIgnoreCase) ||
                version != RegistryConstants.ZeroHourVersionDWord ||
                string.IsNullOrEmpty(serial))
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        // Check if running as administrator - required for HKEY_LOCAL_MACHINE writes
        if (!_registryService.IsRunningAsAdministrator())
        {
            details.Add("✗ Administrator privileges required");
            details.Add("  Registry modifications require elevated permissions");
            return Task.FromResult(new ActionSetResult(false, "Administrator privileges are required to modify registry keys. Please restart GenHub as administrator.", details));
        }

        try
        {
            details.Add("Starting EA App registry configuration...");
            bool allSucceeded = true;
            var failedOperations = new List<string>();

            if (installation.HasGenerals)
            {
                details.Add($"Configuring EA App registry for Generals: {installation.GeneralsPath}");

                if (!_registryService.SetStringValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.InstallPathValueName, installation.GeneralsPath))
                {
                    allSucceeded = false;
                    failedOperations.Add($"{RegistryConstants.EAAppGeneralsKeyPath}\\{RegistryConstants.InstallPathValueName}");
                    details.Add($"  ✗ Failed to set InstallPath");
                }
                else
                {
                    details.Add($"  ✓ InstallPath = {installation.GeneralsPath}");
                }

                if (!_registryService.SetIntValue(RegistryConstants.EAAppGeneralsKeyPath, RegistryConstants.VersionValueName, RegistryConstants.GeneralsVersionDWord))
                {
                    allSucceeded = false;
                    failedOperations.Add($"{RegistryConstants.EAAppGeneralsKeyPath}\\{RegistryConstants.VersionValueName}");
                    details.Add($"  ✗ Failed to set Version");
                }
                else
                {
                    details.Add($"  ✓ Version = {RegistryConstants.GeneralsVersionDWord}");
                }

                var existingSerial = _registryService.GetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty);
                if (string.IsNullOrEmpty(existingSerial))
                {
                    const string defaultSerial = "1234567890";
                    if (!_registryService.SetStringValue(RegistryConstants.EAAppGeneralsErgcKeyPath, string.Empty, defaultSerial))
                    {
                        allSucceeded = false;
                        failedOperations.Add($"{RegistryConstants.EAAppGeneralsErgcKeyPath}\\(Default)");
                        details.Add($"  ✗ Failed to set serial key");
                    }
                    else
                    {
                        details.Add($"  ✓ Serial key created: {defaultSerial}");
                    }
                }
                else
                {
                    details.Add($"  ✓ Serial key already exists");
                }

                if (allSucceeded)
                {
                    details.Add("✓ Generals registry configuration completed");
                }
            }

            if (installation.HasZeroHour)
            {
                details.Add($"Configuring EA App registry for Zero Hour: {installation.ZeroHourPath}");

                if (!_registryService.SetStringValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.InstallPathValueName, installation.ZeroHourPath))
                {
                    allSucceeded = false;
                    failedOperations.Add($"{RegistryConstants.EAAppZeroHourKeyPath}\\{RegistryConstants.InstallPathValueName}");
                    details.Add($"  ✗ Failed to set InstallPath");
                }
                else
                {
                    details.Add($"  ✓ InstallPath = {installation.ZeroHourPath}");
                }

                if (!_registryService.SetIntValue(RegistryConstants.EAAppZeroHourKeyPath, RegistryConstants.VersionValueName, RegistryConstants.ZeroHourVersionDWord))
                {
                    allSucceeded = false;
                    failedOperations.Add($"{RegistryConstants.EAAppZeroHourKeyPath}\\{RegistryConstants.VersionValueName}");
                    details.Add($"  ✗ Failed to set Version");
                }
                else
                {
                    details.Add($"  ✓ Version = {RegistryConstants.ZeroHourVersionDWord}");
                }

                var existingSerial = _registryService.GetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty);
                if (string.IsNullOrEmpty(existingSerial))
                {
                    const string defaultSerial = "1234567890";
                    if (!_registryService.SetStringValue(RegistryConstants.EAAppZeroHourErgcKeyPath, string.Empty, defaultSerial))
                    {
                        allSucceeded = false;
                        failedOperations.Add($"{RegistryConstants.EAAppZeroHourErgcKeyPath}\\(Default)");
                        details.Add($"  ✗ Failed to set serial key");
                    }
                    else
                    {
                        details.Add($"  ✓ Serial key created: {defaultSerial}");
                    }
                }
                else
                {
                    details.Add($"  ✓ Serial key already exists");
                }

                if (allSucceeded)
                {
                    details.Add("✓ Zero Hour registry configuration completed");
                }
            }

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
        // Undoing registry fixes is tricky - usually we don't want to revert to a broken state.
        return Task.FromResult(Success());
    }
}
