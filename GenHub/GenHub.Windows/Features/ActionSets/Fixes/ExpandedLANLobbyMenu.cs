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
    public override string Title => "Expanded LAN Lobby Menu (Addon)";

    /// <inheritdoc/>
    public override string Description => "Replaces the LAN multiplayer lobby UI with an expanded widescreen layout (also managed in Downloads).";

    /// <inheritdoc/>
    public override string DetailedDescription => "Replaces the default 4-row LAN lobby interface with a widescreen-adapted layout that displays more games and player names without cramped scrolling. This UI addon can also be downloaded and managed from the Downloads section.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.QualityOfLife;

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
                "Expanded LAN Lobby Menu UI Mod:",
                "• Modifies in-game window definitions and textures for widescreen displays.",
                "• Expands the LAN lobby room list to show more games and player names without cramped scrolling.",
                "• Available as a Community Outpost Addon in Downloads to enable on Game Profiles.",
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
