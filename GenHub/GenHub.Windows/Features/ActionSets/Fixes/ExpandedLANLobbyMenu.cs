namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that provides guidance for expanded LAN lobby menu.
/// This fix explains how to access and use LAN features in Generals and Zero Hour.
/// </summary>
public class ExpandedLANLobbyMenu(ILogger<ExpandedLANLobbyMenu> logger) : BaseActionSet(logger)
{
    private readonly ILogger<ExpandedLANLobbyMenu> _logger = logger;
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", "sub_markers", "ExpandedLANLobbyMenu.done");

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
            _logger.LogError(ex, "Error checking LAN lobby menu status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            // Provide guidance for LAN play
            _logger.LogInformation("LAN Lobby Menu Information:");
            _logger.LogInformation("Generals and Zero Hour have built-in LAN support.");
            _logger.LogInformation(string.Empty);
            _logger.LogInformation("To play on LAN:");
            _logger.LogInformation("1. Ensure all players are on the same network");
            _logger.LogInformation("2. Launch the game");
            _logger.LogInformation("3. Go to 'Multiplayer' > 'Network' > 'LAN'");
            _logger.LogInformation("4. Create or host a LAN game");
            _logger.LogInformation("5. Other players can join from the LAN lobby");
            _logger.LogInformation(string.Empty);
            _logger.LogInformation("Note: For best LAN experience:");
            _logger.LogInformation("- Ensure Windows Firewall allows the game");
            _logger.LogInformation("- Disable VPN if not needed");
            _logger.LogInformation("- Use wired network connection if possible");
            _logger.LogInformation("- Ensure all players have the same game version");
            _logger.LogInformation(string.Empty);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString());
            }
            catch
            {
            }

            return Task.FromResult(new ActionSetResult(true, "LAN lobby menu is built into the game. See logs for details."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying LAN lobby menu fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Expanded LAN Lobby Menu Fix is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
