namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// Fix that applies Windows compatibility flags (Run as Admin, High DPI)
/// and adds Windows Defender exclusions for game executables.
/// </summary>
public class AppCompatConfigurationsFix(
    IRegistryService registryService,
    ILogger<AppCompatConfigurationsFix> logger) : BaseActionSet(logger)
{
    private static readonly string[] GeneralsExecutables = ["Generals.exe", "generals.exe", "generalsv.exe"];
    private static readonly string[] ZeroHourExecutables = ["Generals.exe", "generals.exe", "generalszh.exe", "GeneralsOnlineZH.exe", "GeneralsOnlineZH_30.exe", "GeneralsOnlineZH_60.exe"];

    private readonly IRegistryService _registryService = registryService ?? throw new ArgumentNullException(nameof(registryService));
    private readonly ILogger<AppCompatConfigurationsFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "AppCompatConfigurationsFix";

    /// <inheritdoc/>
    public override string Title => "Windows Compatibility Configurations";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation) => Task.FromResult(true);

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
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
                    var current = _registryService.GetStringValue(RegistryConstants.AppCompatLayersKeyPath, fullPath);
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
                    var current = _registryService.GetStringValue(RegistryConstants.AppCompatLayersKeyPath, fullPath);
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

            if (installation.HasGenerals)
            {
                details.Add($"Processing Generals executables: {installation.GeneralsPath}");
                await ProcessExecutablesAsync(installation.GeneralsPath, GeneralsExecutables, flag, details, ct);
            }

            if (installation.HasZeroHour)
            {
                details.Add($"Processing Zero Hour executables: {installation.ZeroHourPath}");
                await ProcessExecutablesAsync(installation.ZeroHourPath, ZeroHourExecutables, flag, details, ct);
            }

            details.Add("✓ Windows compatibility configuration completed successfully");
            return new ActionSetResult(true, null, details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply AppCompat configurations");
            details.Add($"✗ Error: {ex.Message}");
            return new ActionSetResult(false, ex.Message, details);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Undoing Windows Compatibility Configurations is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private async Task ProcessExecutablesAsync(string installPath, string[] executables, string flag, List<string> details, CancellationToken ct)
    {
        int processedCount = 0;
        int defenderCount = 0;

        foreach (var exe in executables)
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = Path.Combine(installPath, exe);
            if (!File.Exists(fullPath)) continue;

            // 1. Set Registry AppCompat Flag
            try
            {
                _registryService.SetStringValue(RegistryConstants.AppCompatLayersKeyPath, fullPath, flag);
                details.Add($"  ✓ Set compatibility flags for: {exe}");
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set registry flag for {Path}", fullPath);
                details.Add($"  ✗ Failed to set flags for: {exe}");
            }

            // 2. Add Windows Defender Exclusion
            var defenderResult = await AddDefenderExclusionAsync(fullPath, ct);
            if (defenderResult)
            {
                details.Add($"  ✓ Added Windows Defender exclusion for: {exe}");
                defenderCount++;
            }
            else
            {
                details.Add($"  ⚠ Could not add Defender exclusion for: {exe}");
            }
        }

        details.Add($"✓ Processed {processedCount} executables");
        details.Add($"✓ Added {defenderCount} Windows Defender exclusions");
    }

    private async Task<bool> AddDefenderExclusionAsync(string path, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-WindowStyle Hidden -NoProfile -NonInteractive -Command \"Add-MpPreference -ExclusionPath \\\"{path}\\\"\"",
                CreateNoWindow = true,
                UseShellExecute = true, // Required for admin prompt if not already admin
                Verb = "runas",
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(ct);
                return process.ExitCode == 0;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add Defender exclusion for {Path}", path);
            return false;
        }
    }
}
