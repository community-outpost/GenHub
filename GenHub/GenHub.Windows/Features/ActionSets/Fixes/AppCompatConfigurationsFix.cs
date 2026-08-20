namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that applies Windows compatibility flags (Run as Admin, High DPI) for game executables.
/// </summary>
public class AppCompatConfigurationsFix(
    IRegistryService registryService,
    ILogger<AppCompatConfigurationsFix> logger) : BaseActionSet(logger)
{
    private static readonly IReadOnlyList<string> GeneralsExecutables = ["Generals.exe", "generals.exe", "generalsv.exe"];
    private static readonly IReadOnlyList<string> ZeroHourExecutables = ["Generals.exe", "generals.exe", "generalszh.exe", "GeneralsOnlineZH.exe", "GeneralsOnlineZH_30.exe", "GeneralsOnlineZH_60.exe"];

    /// <inheritdoc/>
    public override string Id => "AppCompatConfigurationsFix";

    /// <inheritdoc/>
    public override string Title => "Windows Compatibility Configurations";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        string expectedFlag = installation.InstallationType == GameInstallationType.Steam
            ? "~ HIGHDPIAWARE"
            : "~ RUNASADMIN HIGHDPIAWARE";

        if (installation.HasGenerals)
        {
            foreach (var exe in GeneralsExecutables)
            {
                var fullPath = Path.Combine(installation.GeneralsPath, exe);
                if (File.Exists(fullPath))
                {
                    var current = registryService.GetStringValue(RegistryConstants.AppCompatLayersKeyPath, fullPath);
                    if (current != expectedFlag) return Task.FromResult(false);
                }
            }
        }

        if (installation.HasZeroHour)
        {
            foreach (var exe in ZeroHourExecutables)
            {
                var fullPath = Path.Combine(installation.ZeroHourPath, exe);
                if (File.Exists(fullPath))
                {
                    var current = registryService.GetStringValue(RegistryConstants.AppCompatLayersKeyPath, fullPath);
                    if (current != expectedFlag) return Task.FromResult(false);
                }
            }
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    protected override async Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            details.Add("Starting Windows compatibility configuration...");

            string flag = installation.InstallationType == GameInstallationType.Steam
                ? "~ HIGHDPIAWARE"
                : "~ RUNASADMIN HIGHDPIAWARE";

            details.Add($"Installation type: {installation.InstallationType}");
            details.Add($"Compatibility flags: {flag}");
            details.Add(string.Empty);

            bool allSucceeded = true;

            if (installation.HasGenerals)
            {
                details.Add($"Processing Generals executables: {installation.GeneralsPath}");
                var ok = await ProcessExecutablesAsync(installation.GeneralsPath, GeneralsExecutables, flag, details, ct);
                if (!ok) allSucceeded = false;
            }

            if (installation.HasZeroHour)
            {
                details.Add($"Processing Zero Hour executables: {installation.ZeroHourPath}");
                var ok = await ProcessExecutablesAsync(installation.ZeroHourPath, ZeroHourExecutables, flag, details, ct);
                if (!ok) allSucceeded = false;
            }

            if (!allSucceeded)
            {
                return new ActionSetResult(false, "Failed to apply compatibility flags to one or more executables.", details);
            }

            details.Add("✓ Windows compatibility configuration completed successfully");
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply AppCompat configurations");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();
        try
        {
            details.Add("Removing Windows compatibility registry flags...");

            if (installation.HasGenerals)
            {
                foreach (var exe in GeneralsExecutables)
                {
                    var fullPath = Path.Combine(installation.GeneralsPath, exe);
                    if (registryService.DeleteValue(RegistryConstants.AppCompatLayersKeyPath, fullPath))
                    {
                        details.Add($"  ✓ Removed compatibility flags for: {exe}");
                    }
                }
            }

            if (installation.HasZeroHour)
            {
                foreach (var exe in ZeroHourExecutables)
                {
                    var fullPath = Path.Combine(installation.ZeroHourPath, exe);
                    if (registryService.DeleteValue(RegistryConstants.AppCompatLayersKeyPath, fullPath))
                    {
                        details.Add($"  ✓ Removed compatibility flags for: {exe}");
                    }
                }
            }

            details.Add("✓ Compatibility flags removed successfully");
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to undo AppCompat configurations");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    private Task<bool> ProcessExecutablesAsync(string installPath, IReadOnlyList<string> executables, string flag, List<string> details, CancellationToken ct)
    {
        int processedCount = 0;
        bool allSucceeded = true;

        foreach (var exe in executables)
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = Path.Combine(installPath, exe);
            if (!File.Exists(fullPath)) continue;

            // Set Registry AppCompat Flag
            try
            {
                if (registryService.SetStringValue(RegistryConstants.AppCompatLayersKeyPath, fullPath, flag))
                {
                    details.Add($"  ✓ Set compatibility flags for: {exe}");
                    processedCount++;
                }
                else
                {
                    allSucceeded = false;
                    details.Add($"  ✗ Failed to set flags for: {exe}");
                }
            }
            catch (Exception ex)
            {
                allSucceeded = false;
                logger.LogWarning(ex, "Failed to set registry flag for {Path}", fullPath);
                details.Add($"  ✗ Failed to set flags for: {exe}");
            }
        }

        details.Add($"✓ Processed {processedCount} executables");
        return Task.FromResult(allSucceeded);
    }
}
