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
    private static readonly string[] GeneralsExecutables = ["Generals.exe", "generals.exe"];
    private static readonly string[] ZeroHourExecutables = ["game.exe", "Game.exe"];
    private readonly ILogger<GameRangerRunAsAdmin> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "GameRangerRunAsAdmin";

    /// <inheritdoc/>
    public override string Title => "GameRanger Run as Administrator";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Only applicable if GameRanger IS installed
        var gameRangerInstalled = IsGameRangerInstalled();
        return Task.FromResult(gameRangerInstalled && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
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
            _logger.LogError(ex, "Error checking GameRanger compatibility status");
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
                _logger.LogInformation("GameRanger is not installed. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            // Check if admin compatibility is already set
            if (HasAdminCompatibility(installation))
            {
                _logger.LogInformation("Game executables already have run as administrator compatibility.");
                return Task.FromResult(new ActionSetResult(true));
            }

            // Provide guidance for GameRanger
            _logger.LogWarning("GameRanger is installed. Games should run as administrator for GameRanger compatibility.");
            _logger.LogInformation("To configure GameRanger:");
            _logger.LogInformation("1. Open GameRanger");
            _logger.LogInformation("2. Go to 'Edit' > 'Game Settings'");
            _logger.LogInformation("3. Select Generals or Zero Hour");
            _logger.LogInformation("4. Check 'Run this program as an administrator' option");
            _logger.LogInformation("5. Ensure it is enabled");
            _logger.LogInformation(string.Empty);
            _logger.LogInformation("Alternatively, you can:");
            _logger.LogInformation("- Right-click on game executable");
            _logger.LogInformation("- Select 'Properties'");
            _logger.LogInformation("- Go to 'Compatibility' tab");
            _logger.LogInformation("- Check 'Run this program as an administrator'");
            _logger.LogInformation("- Click 'Apply' and 'OK'");
            _logger.LogInformation("Alternatively, you can:");
            _logger.LogInformation("- Configure Windows to always run games as administrator");
            _logger.LogInformation("- Use compatibility mode if available");

            return Task.FromResult(new ActionSetResult(true, "Please configure GameRanger to run games as administrator. See logs for details."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying GameRanger compatibility fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("GameRanger Run as Administrator Fix is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private bool IsGameRangerInstalled()
    {
        try
        {
            // Check for GameRanger in registry
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                false);

            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName, false);
                    if (subKey != null)
                    {
                        if (subKey.GetValue("DisplayName") is string displayName && displayName.Contains("GameRanger", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }

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
            _logger.LogWarning(ex, "Error checking for GameRanger installation");
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
                executables.AddRange(GeneralsExecutables);
            }

            if (installation.HasZeroHour)
            {
                executables.AddRange(ZeroHourExecutables);
            }

            foreach (var exe in executables)
            {
                var exePath = exe.Equals("game.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("Game.exe", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(installation.ZeroHourPath, exe)
                    : Path.Combine(installation.GeneralsPath, exe);

                if (!File.Exists(exePath))
                {
                    continue;
                }

                // Check for compatibility flags in AppCompat registry
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
                    false);

                if (key != null)
                {
                    if (key.GetValue(exePath) is string flags && flags.Contains("RUNASADMIN", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking admin compatibility");
            return false;
        }
    }
}
