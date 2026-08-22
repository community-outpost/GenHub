namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
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
    /// <inheritdoc/>
    public override string Id => "NahimicFix";

    /// <inheritdoc/>
    public override string Title => "Nahimic Audio Compatibility";

    /// <inheritdoc/>
    public override string Description => "Detects problematic Nahimic audio services that cause startup crashes and provides guidance to disable them.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Nahimic audio enhancement software hooks into older DirectX 8 audio pipelines, causing Generals and Zero Hour to freeze or crash on launch. This fix scans running services for Nahimic drivers and guides you through disabling the background service.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.Compatibility;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // Only applicable if Nahimic is actually installed (something to check/warn about)
        var nahimicInstalled = IsNahimicInstalled();
        return Task.FromResult(nahimicInstalled && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // This is an informational fix - always returns false since it requires manual action
        // Users must manually disable Nahimic service
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
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
                logger.LogInformation("Nahimic audio driver is not installed. No action needed.");
                return Task.FromResult(new ActionSetResult(true, null, details));
            }

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

            logger.LogWarning("Nahimic audio driver is installed. This may cause audio issues with Generals/Zero Hour. Please disable Nahimic Service in Windows Services or Task Manager.");

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying Nahimic compatibility fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        logger.LogWarning("Nahimic Fix is informational only. No undo action needed.");
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
                    if (subKey?.GetValue("DisplayName") is string displayName && displayName.Contains("Nahimic", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            // Check for Nahimic processes
            var p1 = Process.GetProcessesByName("Nahimic");
            try
            {
                if (p1.Length > 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var p in p1) p.Dispose();
            }

            var p2 = Process.GetProcessesByName("NahimicService");
            try
            {
                return p2.Length > 0;
            }
            finally
            {
                foreach (var p in p2) p.Dispose();
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
