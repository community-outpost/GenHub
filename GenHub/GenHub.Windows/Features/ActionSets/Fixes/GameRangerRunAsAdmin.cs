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
/// Fix that provides GameRanger compatibility guidance.
/// GameRanger requires games to run as administrator for proper functionality.
/// </summary>
public class GameRangerRunAsAdmin(ILogger<GameRangerRunAsAdmin> logger) : BaseActionSet(logger)
{
    private static readonly IReadOnlyList<string> GeneralsExecutables = ["Generals.exe", "generals.exe"];
    private static readonly IReadOnlyList<string> ZeroHourExecutables = ["generals.exe", "game.dat", "game.exe", "generalszh.exe"];

    /// <inheritdoc/>
    public override string Id => "GameRangerRunAsAdmin";

    /// <inheritdoc/>
    public override string Title => "GameRanger Run as Administrator";

    /// <inheritdoc/>
    public override string Description => "Verifies GameRanger integration and guides configuring administrator privileges to allow GameRanger to launch multiplayer lobbies.";

    /// <inheritdoc/>
    public override string DetailedDescription => "When playing via the GameRanger client, the game executable must run with administrator privileges so GameRanger can inject its room and network parameters. This fix detects GameRanger installations and verifies compatibility flags to prevent launch freezes.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.Multiplayer;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // Only applicable if GameRanger IS installed
        var gameRangerInstalled = IsGameRangerInstalled();
        return Task.FromResult(gameRangerInstalled && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            // Check if GameRanger is installed
            var gameRangerInstalled = IsGameRangerInstalled();

            if (!gameRangerInstalled)
            {
                // If GameRanger is not installed, it's not applied (it's N/A)
                return Task.FromResult(false);
            }

            // Check if game executables have run as admin compatibility
            var hasAdminCompat = HasAdminCompatibility(installation);

            return Task.FromResult(hasAdminCompat);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking GameRanger compatibility status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            var gameRangerInstalled = IsGameRangerInstalled();

            if (!gameRangerInstalled)
            {
                logger.LogInformation("GameRanger is not installed. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            // Check if admin compatibility is already set
            if (HasAdminCompatibility(installation))
            {
                logger.LogInformation("Game executables already have run as administrator compatibility.");
                return Task.FromResult(new ActionSetResult(true));
            }

            // Provide guidance for GameRanger
            logger.LogWarning("GameRanger is installed. Games should run as administrator for GameRanger compatibility.");
            logger.LogInformation("To configure GameRanger:");
            logger.LogInformation("1. Open GameRanger");
            logger.LogInformation("2. Go to 'Edit' > 'Game Settings'");
            logger.LogInformation("3. Select Generals or Zero Hour");
            logger.LogInformation("4. Check 'Run this program as an administrator' option");
            logger.LogInformation("5. Ensure it is enabled");
            logger.LogInformation(string.Empty);
            logger.LogInformation("Alternatively, you can:");
            logger.LogInformation("- Right-click on game executable");
            logger.LogInformation("- Select 'Properties'");
            logger.LogInformation("- Go to 'Compatibility' tab");
            logger.LogInformation("- Check 'Run this program as an administrator'");
            logger.LogInformation("- Click 'Apply' and 'OK'");

            return Task.FromResult(new ActionSetResult(true, null, ["Please configure GameRanger to run games as administrator. See logs for details."]));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying GameRanger compatibility fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("GameRanger Run as Administrator Fix is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private static bool CheckUninstallKey(Microsoft.Win32.RegistryKey baseKey, string subPath)
    {
        using var key = baseKey.OpenSubKey(subPath, false);
        if (key != null)
        {
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(subKeyName, false);
                if (subKey?.GetValue(RegistryConstants.DisplayNameValueName) is string displayName && displayName.Contains("GameRanger", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsGameRangerInstalled()
    {
        try
        {
            // Check for GameRanger in registry (HKLM, WOW6432Node, HKCU)
            if (CheckUninstallKey(Microsoft.Win32.Registry.LocalMachine, RegistryConstants.UninstallKeyPath)) return true;
            if (CheckUninstallKey(Microsoft.Win32.Registry.LocalMachine, RegistryConstants.UninstallKeyPathWow64)) return true;
            if (CheckUninstallKey(Microsoft.Win32.Registry.CurrentUser, RegistryConstants.UninstallKeyPath)) return true;

            // Check for GameRanger processes
            var processes = Process.GetProcessesByName("GameRanger");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking for GameRanger installation");
            return false;
        }
    }

    private bool HasAdminCompatibility(GameInstallation installation)
    {
        try
        {
            var executables = new List<string>();

            if (installation.HasGenerals)
            {
                foreach (var exe in GeneralsExecutables)
                {
                    var full = Path.Combine(installation.GeneralsPath, exe);
                    if (File.Exists(full)) executables.Add(full);
                }
            }

            if (installation.HasZeroHour)
            {
                foreach (var exe in ZeroHourExecutables)
                {
                    var full = Path.Combine(installation.ZeroHourPath, exe);
                    if (File.Exists(full)) executables.Add(full);
                }
            }

            using var hklmKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RegistryConstants.AppCompatLayersKeyPath, false);
            using var hkcuKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryConstants.AppCompatLayersKeyPath, false);

            foreach (var exePath in executables)
            {
                if (hklmKey?.GetValue(exePath) is string hklmFlags && hklmFlags.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (hkcuKey?.GetValue(exePath) is string hkcuFlags && hkcuFlags.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking admin compatibility");
            return false;
        }
    }
}
