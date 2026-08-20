namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

/// <summary>
/// Fix that creates registry entries for C&amp;C Online (Revora) multiplayer service.
/// This enables the game to properly detect and connect to C&amp;C Online servers.
/// </summary>
public class CncOnlineLauncherFix(
    IRegistryService registryService,
    ILogger<CncOnlineLauncherFix> logger) : BaseActionSet(logger)
{
    /// <inheritdoc/>
    public override string Id => "CncOnlineLauncherFix";

    /// <inheritdoc/>
    public override string Title => "C&C Online Launcher Fix";

    /// <inheritdoc/>
    public override string Description => "Configures Revora C&C:Online registry keys so community multiplayer services can detect and launch your game.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Since EA GameSpy servers were decommissioned, C&C:Online provides the primary multiplayer network for Generals and Zero Hour. This fix writes the necessary installation path and version metadata into the registry so community launcher hooks can direct multiplayer traffic to active community servers.";

    /// <inheritdoc/>
    public override string Category => "Multiplayer";

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
            // Check if C&C Online registry entries exist in HKCU
            var cncOnlineInstalled = registryService.GetStringValue(
                RegistryConstants.CncOnlineKeyPath,
                RegistryConstants.InstallPathValueName,
                useWow6432Node: true,
                hive: RegistryHive.CurrentUser);

            return Task.FromResult(!string.IsNullOrEmpty(cncOnlineInstalled));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking C&C Online registry status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            details.Add("Starting C&C Online registry configuration...");
            bool allSucceeded = true;

            // Create C&C Online registry entries for Generals
            if (installation.HasGenerals)
            {
                details.Add($"Configuring C&C Online for Generals at: {installation.GeneralsPath}");

                bool ok1 = registryService.SetStringValue(
                    RegistryConstants.CncOnlineGeneralsKeyPath,
                    RegistryConstants.InstallPathValueName,
                    installation.GeneralsPath,
                    useWow6432Node: true,
                    hive: RegistryHive.CurrentUser);

                bool ok2 = registryService.SetStringValue(
                    RegistryConstants.CncOnlineGeneralsKeyPath,
                    RegistryConstants.VersionValueName,
                    RegistryConstants.CncOnlineGeneralsVersion,
                    useWow6432Node: true,
                    hive: RegistryHive.CurrentUser);

                if (ok1 && ok2)
                {
                    details.Add("✓ Created: HKCU\\SOFTWARE\\Revora\\CNCOnline\\Generals");
                    details.Add($"  • InstallPath = {installation.GeneralsPath}");
                    details.Add($"  • Version = {RegistryConstants.CncOnlineGeneralsVersion}");
                    logger.LogInformation("Created C&C Online registry entries for Generals");
                }
                else
                {
                    allSucceeded = false;
                    details.Add("✗ Failed to write C&C Online registry entries for Generals");
                }
            }

            // Create C&C Online registry entries for Zero Hour
            if (installation.HasZeroHour)
            {
                details.Add($"Configuring C&C Online for Zero Hour at: {installation.ZeroHourPath}");

                bool ok1 = registryService.SetStringValue(
                    RegistryConstants.CncOnlineZeroHourKeyPath,
                    RegistryConstants.InstallPathValueName,
                    installation.ZeroHourPath,
                    useWow6432Node: true,
                    hive: RegistryHive.CurrentUser);

                bool ok2 = registryService.SetStringValue(
                    RegistryConstants.CncOnlineZeroHourKeyPath,
                    RegistryConstants.VersionValueName,
                    RegistryConstants.CncOnlineZeroHourVersion,
                    useWow6432Node: true,
                    hive: RegistryHive.CurrentUser);

                if (ok1 && ok2)
                {
                    details.Add("✓ Created: HKCU\\SOFTWARE\\Revora\\CNCOnline\\ZeroHour");
                    details.Add($"  • InstallPath = {installation.ZeroHourPath}");
                    details.Add($"  • Version = {RegistryConstants.CncOnlineZeroHourVersion}");
                    logger.LogInformation("Created C&C Online registry entries for Zero Hour");
                }
                else
                {
                    allSucceeded = false;
                    details.Add("✗ Failed to write C&C Online registry entries for Zero Hour");
                }
            }

            // Create main C&C Online entry if a valid base path exists
            var basePath = installation.HasGenerals
                ? installation.GeneralsPath
                : installation.ZeroHourPath;

            if (!string.IsNullOrEmpty(basePath))
            {
                details.Add("Creating main C&C Online registry entry...");

                bool mainOk1 = registryService.SetStringValue(
                    RegistryConstants.CncOnlineKeyPath,
                    RegistryConstants.InstallPathValueName,
                    basePath,
                    useWow6432Node: true,
                    hive: RegistryHive.CurrentUser);

                bool mainOk2 = registryService.SetStringValue(
                    RegistryConstants.CncOnlineKeyPath,
                    RegistryConstants.VersionValueName,
                    RegistryConstants.CncOnlineVersion,
                    useWow6432Node: true,
                    hive: RegistryHive.CurrentUser);

                if (mainOk1 && mainOk2)
                {
                    details.Add("✓ Created: HKCU\\SOFTWARE\\Revora\\CNCOnline");
                    details.Add($"  • InstallPath = {basePath}");
                    details.Add($"  • Version = {RegistryConstants.CncOnlineVersion}");
                }
                else
                {
                    allSucceeded = false;
                    details.Add("✗ Failed to write main C&C Online registry entries");
                }
            }

            if (!allSucceeded)
            {
                return Task.FromResult(new ActionSetResult(false, "Failed to write one or more C&C Online registry entries.", details));
            }

            details.Add("✓ C&C Online registry configuration completed successfully");
            logger.LogInformation("C&C Online registry fix applied with {DetailCount} actions", details.Count);
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying C&C Online registry fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Undoing C&C Online Registry Fix is not recommended as it may break multiplayer functionality.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
