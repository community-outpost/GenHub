namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that provides Nahimic audio compatibility guidance.
/// Nahimic audio drivers can cause audio issues with older games.
/// This fix checks for Nahimic installation and provides guidance.
/// </summary>
public class NahimicFix(ILogger<NahimicFix> logger) : BaseActionSet(logger)
{
    private readonly ILogger<NahimicFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "NahimicFix";

    /// <inheritdoc/>
    public override string Title => "Nahimic Audio Compatibility";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Only applicable if Nahimic is actually installed (something to check/warn about)
        var nahimicInstalled = IsNahimicInstalled();
        return Task.FromResult(nahimicInstalled && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        // This is an informational fix - always returns false since it requires manual action
        // Users must manually disable Nahimic service
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            details.Add("Nahimic Audio Compatibility - Informational");
            details.Add(string.Empty);

            var nahimicInstalled = IsNahimicInstalled();

            if (!nahimicInstalled)
            {
                details.Add("✓ Nahimic audio driver is not installed");
                details.Add("  No action needed");
                _logger.LogInformation("Nahimic audio driver is not installed. No action needed.");
                return Task.FromResult(new ActionSetResult(true, null, details));
            }

            // Provide guidance for disabling Nahimic
            details.Add("⚠ Nahimic audio driver detected");
            details.Add("  This may cause audio issues with Generals/Zero Hour");
            details.Add(string.Empty);
            details.Add("To disable Nahimic audio effects:");
            details.Add("  1. Open Task Manager (Ctrl+Shift+Esc)");
            details.Add("  2. Go to the 'Services' tab");
            details.Add("  3. Find 'Nahimic Service' or 'Nahimic Service UI'");
            details.Add("  4. Right-click and select 'Stop'");
            details.Add("  5. Right-click again and select 'Properties'");
            details.Add("  6. Change 'Startup type' to 'Disabled'");
            details.Add("  7. Click 'Apply' and 'OK'");
            details.Add(string.Empty);
            details.Add("Alternative: Uninstall Nahimic if you don't need it");

            _logger.LogWarning("Nahimic audio driver is installed. This may cause audio issues with Generals/Zero Hour.");
            _logger.LogInformation("To disable Nahimic audio effects:");
            _logger.LogInformation("1. Open Task Manager (Ctrl+Shift+Esc)");
            _logger.LogInformation("2. Go to the 'Services' tab");
            _logger.LogInformation("3. Find 'Nahimic Service' or 'Nahimic Service UI'");
            _logger.LogInformation("4. Right-click and select 'Stop'");
            _logger.LogInformation("5. Right-click again and select 'Properties'");
            _logger.LogInformation("6. Change 'Startup type' to 'Disabled'");
            _logger.LogInformation("7. Click 'Apply' and 'OK'");
            _logger.LogInformation(string.Empty);
            _logger.LogInformation("Alternatively, you can uninstall Nahimic audio software if you don't need it.");

            return Task.FromResult(new ActionSetResult(true, "Please manually disable Nahimic service. See details for instructions.", details));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying Nahimic compatibility fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Nahimic Fix is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private static bool IsNahimicInstalled()
    {
        try
        {
            // Check for Nahimic in registry
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                RegistryConstants.UninstallKeyPath,
                false);

            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName, false);
                    if (subKey != null)
                    {
                        if (subKey.GetValue("DisplayName") is string displayName && displayName.Contains("Nahimic", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

            // Check for Nahimic processes
            var processes = Process.GetProcessesByName("Nahimic");
            if (processes.Length > 0)
            {
                return true;
            }

            processes = Process.GetProcessesByName("NahimicService");
            return processes.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
