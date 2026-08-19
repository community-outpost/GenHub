namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
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
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        return Task.FromResult(installation.HasGenerals || installation.HasZeroHour);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
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
            // Provide guidance for LAN play
            logger.LogInformation("LAN Lobby Menu Information:");
            logger.LogInformation("Generals and Zero Hour have built-in LAN support.");
            logger.LogInformation(string.Empty);
            logger.LogInformation("To play on LAN:");
            logger.LogInformation("1. Ensure all players are on the same network");
            logger.LogInformation("2. Launch the game");
            logger.LogInformation("3. Go to 'Multiplayer' > 'Network' > 'LAN'");
            logger.LogInformation("4. Create or host a LAN game");
            logger.LogInformation("5. Other players can join from the LAN lobby");
            logger.LogInformation(string.Empty);
            logger.LogInformation("Note: For best LAN experience:");
            logger.LogInformation("- Ensure Windows Firewall allows the game");
            logger.LogInformation("- Disable VPN if not needed");
            logger.LogInformation("- Use wired network connection if possible");
            logger.LogInformation("- Ensure all players have the same game version");
            logger.LogInformation(string.Empty);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create marker file for ExpandedLANLobbyMenu");
            }

            return Task.FromResult(new ActionSetResult(true, null, ["LAN lobby menu is built into the game. See logs for details."]));
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
        logger.LogWarning("Expanded LAN Lobby Menu Fix is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
