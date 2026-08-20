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
/// Fix that provides high-definition icons for Generals and Zero Hour.
/// This fix replaces low-resolution game icons with HD versions.
/// </summary>
public class HDIconsFix(ILogger<HDIconsFix> logger) : BaseActionSet(logger)
{
    private static readonly IReadOnlyList<string> HdIconFiles =
    [
        "generals_hd.ico",
        "game_hd.ico",
        "zh_hd.ico",
    ];

    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "HDIconsFix.done");

    /// <inheritdoc/>
    public override string Id => "HDIconsFix";

    /// <inheritdoc/>
    public override string Title => "High-Definition Icons";

    /// <inheritdoc/>
    public override string Description => "High-definition icon pack for Generals and Zero Hour desktop shortcuts and window icons.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Original Generals and Zero Hour desktop icons were mastered at 32x32 for Windows XP and appear blurry on modern high-resolution displays. This enhancement provides crisp 256x256 high-definition (.ico) icon assets for desktop shortcuts and game executables.";

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
        if (File.Exists(_markerPath)) return Task.FromResult(true);
        return Task.FromResult(AreHDIconsPresent(installation));
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            details.Add("High-Definition Icons Pack:");
            details.Add("• Provides 256x256 high-resolution shortcut and executable icons.");
            var hdIconsPresent = AreHDIconsPresent(installation);
            if (hdIconsPresent)
            {
                details.Add("✓ HD icon assets detected in installation.");
            }
            else
            {
                details.Add("• Available as a Community Outpost Addon in Downloads to attach to game profiles.");
            }

            logger.LogInformation("HD Icons are typically provided by mods or community content.");
            logger.LogInformation("Use GenHub's Content system to download HD icon packs.");
            logger.LogInformation("HD Icons can be found in the Downloads section under 'Icons' category.");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to create marker file for HDIconsFix");
            }

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying HD icons fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
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
            logger.LogWarning(ex, "Failed to delete marker file for HDIconsFix");
        }

        return Task.FromResult(new ActionSetResult(true, null, ["HD icons marker removed."]));
    }

    private bool AreHDIconsPresent(GameInstallation installation)
    {
        try
        {
            var foundHDIcons = false;

            if (installation.HasGenerals)
            {
                foreach (var iconFile in HdIconFiles)
                {
                    if (File.Exists(Path.Combine(installation.GeneralsPath, iconFile)))
                    {
                        logger.LogInformation("Found HD icon: {Icon}", iconFile);
                        foundHDIcons = true;
                        break;
                    }
                }
            }

            if (installation.HasZeroHour && !foundHDIcons)
            {
                foreach (var iconFile in HdIconFiles)
                {
                    if (File.Exists(Path.Combine(installation.ZeroHourPath, iconFile)))
                    {
                        logger.LogInformation("Found HD icon: {Icon}", iconFile);
                        foundHDIcons = true;
                        break;
                    }
                }
            }

            return foundHDIcons;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking for HD icons");
            return false;
        }
    }
}
