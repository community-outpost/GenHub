namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that disables Origin in-game overlay for Generals and Zero Hour.
/// The Origin overlay can cause performance issues and conflicts with the game.
/// </summary>
public class DisableOriginInGame(ILogger<DisableOriginInGame> logger) : BaseActionSet(logger)
{
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "DisableOriginInGame.done");

    /// <inheritdoc/>
    public override string Id => "DisableOriginInGame";

    /// <inheritdoc/>
    public override string Title => "Disable Origin In-Game Overlay";

    /// <inheritdoc/>
    public override string Description => "Detects if the Origin in-game overlay is active and guides disabling it to prevent rendering conflicts and crashes.";

    /// <inheritdoc/>
    public override string DetailedDescription => "The legacy Origin overlay attempts to hook into the game's 32-bit DirectX 8 graphics pipeline, causing frame drops, mouse desync, and startup crashes. This fix checks your Origin configuration (Origin.ini) and provides instructions on disabling the overlay.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.Compatibility;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // Only applicable if Origin is actually installed (something to disable)
        var originInstalled = IsOriginInstalled();
        return Task.FromResult(originInstalled && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(IsOriginOverlayDisabled() || File.Exists(_markerPath));
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        try
        {
            var originInstalled = IsOriginInstalled();

            if (!originInstalled)
            {
                logger.LogInformation("Origin is not installed. No action needed.");
                return Task.FromResult(new ActionSetResult(true, null, ["Origin is not installed. No action needed."]));
            }

            if (IsOriginOverlayDisabled())
            {
                logger.LogInformation("Origin in-game overlay is already disabled.");
                WriteMarkerFile(_markerPath);
                return Task.FromResult(new ActionSetResult(true, null, ["Origin in-game overlay is already disabled."]));
            }

            logger.LogWarning("Origin in-game overlay is enabled. Please disable it in Origin Application Settings > Origin In-Game.");

            WriteMarkerFile(_markerPath);

            return Task.FromResult(new ActionSetResult(true, null, [
                "Please manually disable Origin in-game overlay in Origin Application Settings > Origin In-Game > Uncheck Enable Origin In-Game."
            ]));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying Origin overlay disable fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        DeleteMarkerFile(_markerPath);
        return Task.FromResult(new ActionSetResult(true, null, ["Origin overlay marker removed."]));
    }

    private bool IsOriginInstalled()
    {
        try
        {
            // Check for Origin in 64-bit and WOW64 registry views
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RegistryConstants.OriginKeyPath, false))
            {
                if (key != null) return true;
            }

            using (var wowKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RegistryConstants.OriginKeyPathWow64, false))
            {
                if (wowKey != null) return true;
            }

            // Check for Origin processes
            var processes = Process.GetProcessesByName("Origin");
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
            logger.LogWarning(ex, "Error checking for Origin installation");
            return false;
        }
    }

    private bool IsOriginOverlayDisabled()
    {
        try
        {
            // Check Origin configuration file
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var originConfigPath = Path.Combine(localAppData, "Origin", "Origin.ini");

            if (!File.Exists(originConfigPath))
            {
                return false;
            }

            var lines = File.ReadAllLines(originConfigPath);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith(';') || line.StartsWith('#') || string.IsNullOrEmpty(line))
                {
                    continue;
                }

                var parts = line.Split('=', 2);
                if (parts.Length == 2 && parts[0].Trim().Equals("OverlayEnabled", StringComparison.OrdinalIgnoreCase))
                {
                    var val = parts[1].Trim();
                    return val.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                           val.Equals("false", StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking Origin overlay configuration");
            return false;
        }
    }
}
