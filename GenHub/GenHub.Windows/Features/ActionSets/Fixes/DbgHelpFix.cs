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
/// Fix for the dbghelp.dll which causes crashes on modern systems.
/// </summary>
public class DbgHelpFix(ILogger<DbgHelpFix> logger) : BaseActionSet(logger)
{
    // Use constants from GameClientConstants
    private const string DbgHelpDll = GameClientConstants.DbgHelpDll;
    private const string DbgHelpDllBak = GameClientConstants.DbgHelpDllBak;

    /// <inheritdoc/>
    public override string Id => "DbgHelpFix";

    /// <inheritdoc/>
    public override string Title => "Debug Help DLL Fix";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Applicable if the file exists in either Generals or Zero Hour path
        // This fix is needed because the old dbghelp.dll causes crashes on modern Windows
        if (installation.HasGenerals && File.Exists(Path.Combine(installation.GeneralsPath, DbgHelpDll)))
        {
            return Task.FromResult(true);
        }

        if (installation.HasZeroHour && File.Exists(Path.Combine(installation.ZeroHourPath, DbgHelpDll)))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        // Considered applied if the DLL is missing (renamed) in all present installations
        bool generalsOk = !installation.HasGenerals || !File.Exists(Path.Combine(installation.GeneralsPath, DbgHelpDll));
        bool zeroHourOk = !installation.HasZeroHour || !File.Exists(Path.Combine(installation.ZeroHourPath, DbgHelpDll));

        return Task.FromResult(generalsOk && zeroHourOk);
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        var details = new List<string>();

        try
        {
            details.Add("Starting DbgHelp.dll fix...");
            details.Add("This DLL causes crashes on modern Windows and will be disabled");

            if (installation.HasGenerals)
            {
                details.Add($"Processing Generals: {installation.GeneralsPath}");
                var result = RenameDll(installation.GeneralsPath, details);
                if (!result)
                {
                    details.Add("  ⚠ DbgHelp.dll not found (may already be fixed)");
                }
            }

            if (installation.HasZeroHour)
            {
                details.Add($"Processing Zero Hour: {installation.ZeroHourPath}");
                var result = RenameDll(installation.ZeroHourPath, details);
                if (!result)
                {
                    details.Add("  ⚠ DbgHelp.dll not found (may already be fixed)");
                }
            }

            details.Add("✓ DbgHelp.dll fix completed successfully");
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
            details.Add("Restoring DbgHelp.dll...");

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

            details.Add("✓ DbgHelp.dll restored");
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
        var dllPath = Path.Combine(path, DbgHelpDll);
        var bakPath = Path.Combine(path, DbgHelpDllBak);

        if (File.Exists(dllPath))
        {
            if (File.Exists(bakPath))
            {
                File.Delete(bakPath);
                details.Add($"  • Deleted existing backup: {DbgHelpDllBak}");
            }

            File.Move(dllPath, bakPath);
            details.Add($"  ✓ Renamed {DbgHelpDll} → {DbgHelpDllBak}");
            return true;
        }

        return false;
    }

    private static void RestoreDll(string path, List<string> details)
    {
        var dllPath = Path.Combine(path, DbgHelpDll);
        var bakPath = Path.Combine(path, DbgHelpDllBak);

        if (File.Exists(bakPath))
        {
            if (File.Exists(dllPath))
            {
                File.Delete(dllPath);
            }

            File.Move(bakPath, dllPath);
            details.Add($"  ✓ Restored {DbgHelpDllBak} → {DbgHelpDll}");
        }
        else
        {
            details.Add($"  ⚠ Backup file not found: {DbgHelpDllBak}");
        }
    }
}
