namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix for the BrowserEngine.dll which causes crashes on modern systems.
/// </summary>
public class BrowserEngineFix(ILogger<BrowserEngineFix> logger) : BaseActionSet(logger)
{
    // Use constants from GameClientConstants
    private const string BrowserEngineDll = GameClientConstants.BrowserEngineDll;
    private const string BrowserEngineDllBak = GameClientConstants.BrowserEngineDllBak;

    /// <inheritdoc/>
    public override string Id => "BrowserEngineFix";

    /// <inheritdoc/>
    public override string Title => "Browser Engine DLL Fix";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Applicable if the file exists in either Generals or Zero Hour path
        if (installation.HasGenerals && File.Exists(Path.Combine(installation.GeneralsPath, BrowserEngineDll)))
        {
            return Task.FromResult(true);
        }

        if (installation.HasZeroHour && File.Exists(Path.Combine(installation.ZeroHourPath, BrowserEngineDll)))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        // Considered applied if the .bak file exists (indicating we renamed it)
        bool generalsApplied = !installation.HasGenerals || File.Exists(Path.Combine(installation.GeneralsPath, BrowserEngineDllBak));
        bool zeroHourApplied = !installation.HasZeroHour || File.Exists(Path.Combine(installation.ZeroHourPath, BrowserEngineDllBak));

        return Task.FromResult(generalsApplied && zeroHourApplied);
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            details.Add("Starting BrowserEngine.dll fix...");
            details.Add("This DLL causes crashes on modern systems and will be disabled");

            if (installation.HasGenerals)
            {
                details.Add($"Processing Generals: {installation.GeneralsPath}");
                var result = RenameDll(installation.GeneralsPath, details);
                if (!result)
                {
                    details.Add("  ⚠ BrowserEngine.dll not found (may already be fixed)");
                }
            }

            if (installation.HasZeroHour)
            {
                details.Add($"Processing Zero Hour: {installation.ZeroHourPath}");
                var result = RenameDll(installation.ZeroHourPath, details);
                if (!result)
                {
                    details.Add("  ⚠ BrowserEngine.dll not found (may already be fixed)");
                }
            }

            details.Add("✓ BrowserEngine.dll fix completed successfully");
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
            details.Add("Restoring BrowserEngine.dll...");

            if (installation.HasGenerals)
            {
                details.Add($"Processing Generals: {installation.GeneralsPath}");
                RestoreDll(installation.GeneralsPath, details);
            }

            if (installation.HasZeroHour)
            {
                details.Add($"Processing Zero Hour: {installation.ZeroHourPath}");
                RestoreDll(installation.ZeroHourPath, details);
            }

            details.Add("✓ BrowserEngine.dll restored");
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    private static bool RenameDll(string path, List<string> details)
    {
        var dllPath = Path.Combine(path, BrowserEngineDll);
        var bakPath = Path.Combine(path, BrowserEngineDllBak);

        if (File.Exists(dllPath))
        {
            if (File.Exists(bakPath))
            {
                File.Delete(bakPath);
                details.Add($"  • Deleted existing backup: {BrowserEngineDllBak}");
            }

            File.Move(dllPath, bakPath);
            details.Add($"  ✓ Renamed {BrowserEngineDll} → {BrowserEngineDllBak}");
            return true;
        }

        return false;
    }

    private static void RestoreDll(string path, List<string> details)
    {
        var dllPath = Path.Combine(path, BrowserEngineDll);
        var bakPath = Path.Combine(path, BrowserEngineDllBak);

        if (File.Exists(bakPath))
        {
            if (File.Exists(dllPath))
            {
                File.Delete(dllPath);
            }

            File.Move(bakPath, dllPath);
            details.Add($"  ✓ Restored {BrowserEngineDllBak} → {BrowserEngineDll}");
        }
        else
        {
            details.Add($"  ⚠ Backup file not found: {BrowserEngineDllBak}");
        }
    }
}
