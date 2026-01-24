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

/// <summary>
/// Fix that creates registry entries for C&amp;C Online (Revora) multiplayer service.
/// This enables the game to properly detect and connect to C&amp;C Online servers.
/// </summary>
public class CncOnlineLauncherFix(
    IRegistryService registryService,
    ILogger<CncOnlineLauncherFix> logger) : BaseActionSet(logger)
{
    private readonly IRegistryService _registryService = registryService;
    private readonly ILogger<CncOnlineLauncherFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "CncOnlineLauncherFix";

    /// <inheritdoc/>
    public override string Title => "C&C Online Launcher Fix";

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
            // Check if C&C Online registry entries exist
            var cncOnlineInstalled = _registryService.GetStringValue(
                RegistryConstants.CncOnlineKeyPath,
                RegistryConstants.InstallPathValueName);

            return Task.FromResult(!string.IsNullOrEmpty(cncOnlineInstalled));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking C&C Online registry status");
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

            // Create C&C Online registry entries for Generals
            if (installation.HasGenerals)
            {
                details.Add($"Configuring C&C Online for Generals at: {installation.GeneralsPath}");

                _registryService.SetStringValue(
                    RegistryConstants.CncOnlineGeneralsKeyPath,
                    RegistryConstants.InstallPathValueName,
                    installation.GeneralsPath);

                _registryService.SetStringValue(
                    RegistryConstants.CncOnlineGeneralsKeyPath,
                    RegistryConstants.VersionValueName,
                    RegistryConstants.CncOnlineGeneralsVersion);

                details.Add("✓ Created: HKCU\\SOFTWARE\\Revora\\CNCOnline\\Generals");
                details.Add($"  • InstallPath = {installation.GeneralsPath}");
                details.Add("  • Version = 1.08");

                _logger.LogInformation("Created C&C Online registry entries for Generals");
            }

            // Create C&C Online registry entries for Zero Hour
            if (installation.HasZeroHour)
            {
                details.Add($"Configuring C&C Online for Zero Hour at: {installation.ZeroHourPath}");

                _registryService.SetStringValue(
                    RegistryConstants.CncOnlineZeroHourKeyPath,
                    RegistryConstants.InstallPathValueName,
                    installation.ZeroHourPath);

                _registryService.SetStringValue(
                    RegistryConstants.CncOnlineZeroHourKeyPath,
                    RegistryConstants.VersionValueName,
                    RegistryConstants.CncOnlineZeroHourVersion);

                details.Add("✓ Created: HKCU\\SOFTWARE\\Revora\\CNCOnline\\ZeroHour");
                details.Add($"  • InstallPath = {installation.ZeroHourPath}");
                details.Add("  • Version = 1.04");

                _logger.LogInformation("Created C&C Online registry entries for Zero Hour");
            }

            // Create main C&C Online entry
            var basePath = installation.HasGenerals
                ? installation.GeneralsPath
                : installation.ZeroHourPath;

            details.Add("Creating main C&C Online registry entry...");

            _registryService.SetStringValue(
                RegistryConstants.CncOnlineKeyPath,
                RegistryConstants.InstallPathValueName,
                basePath);

            _registryService.SetStringValue(
                RegistryConstants.CncOnlineKeyPath,
                RegistryConstants.VersionValueName,
                RegistryConstants.CncOnlineVersion);

            details.Add("✓ Created: HKCU\\SOFTWARE\\Revora\\CNCOnline");
            details.Add($"  • InstallPath = {basePath}");
            details.Add("  • Version = 1.0");
            details.Add("✓ C&C Online registry configuration completed successfully");

            _logger.LogInformation("C&C Online registry fix applied with {DetailCount} actions", details.Count);
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying C&C Online registry fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Undoing C&C Online Registry Fix is not recommended as it may break multiplayer functionality.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
