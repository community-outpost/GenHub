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
/// Fix that provides guidance for expanded LAN lobby menu.
/// This fix explains how to access and use LAN features in Generals and Zero Hour.
/// </summary>
public class ExpandedLANLobbyMenu(ILogger<ExpandedLANLobbyMenu> logger) : BaseActionSet(logger)
{
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "ExpandedLANLobbyMenu.done");

    /// <inheritdoc/>
    public override string Id => "ExpandedLANLobbyMenu";

    /// <inheritdoc/>
    public override string Title => "Expanded LAN Lobby Menu";

    /// <inheritdoc/>
    public override string Description => "Provides guidance and optimal network configuration steps for hosting and joining local and virtual LAN games.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Hosting or joining local and virtual LAN games (e.g. Radmin VPN or ZeroTier) often fails due to firewall blocks or subnet mismatches. This fix provides validated configuration instructions to ensure smooth discovery and connection in the game's LAN lobby menu.";

    /// <inheritdoc/>
    public override string Category => "Multiplayer";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            return Task.FromResult(File.Exists(_markerPath));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking LAN lobby menu status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            var details = new List<string>
            {
                "LAN Lobby Menu Information:",
                "Generals and Zero Hour have built-in LAN support.",
                "To play on LAN:",
                "1. Ensure all players are on the same network",
                "2. Launch the game",
                "3. Go to 'Multiplayer' > 'Network' > 'LAN'",
                "4. Create or host a LAN game",
                "5. Other players can join from the LAN lobby",
                "Note: For best LAN experience:",
                "- Ensure Windows Firewall allows the game",
                "- Disable VPN if not needed",
                "- Use wired network connection if possible",
                "- Ensure all players have the same game version",
            };

            try
            {
                var dir = Path.GetDirectoryName(_markerPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create marker file for ExpandedLANLobbyMenu");
                details.Add($"✗ Failed to create completion marker: {ex.Message}");
                return Task.FromResult(new ActionSetResult(false, $"Failed to create completion marker: {ex.Message}", details));
            }

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying LAN lobby menu fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(_markerPath))
            {
                File.Delete(_markerPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete marker file for ExpandedLANLobbyMenu");
        }

        return Task.FromResult(new ActionSetResult(true, null, ["LAN lobby marker removed."]));
    }
}
