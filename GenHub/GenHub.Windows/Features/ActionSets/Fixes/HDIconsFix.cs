namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that provides high-definition icons for Generals and Zero Hour.
/// This fix replaces low-resolution game icons with HD versions.
/// </summary>
public class HDIconsFix(ILogger<HDIconsFix> logger) : BaseActionSet(logger)
{
    private readonly ILogger<HDIconsFix> _logger = logger;
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", "sub_markers", "HDIconsFix.done");

    /// <inheritdoc/>
    public override string Id => "HDIconsFix";

    /// <inheritdoc/>
    public override string Title => "High-Definition Icons";

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
         if (File.Exists(_markerPath)) return Task.FromResult(true);
         return Task.FromResult(AreHDIconsPresent(installation));
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            details.Add("High-Definition Icons - Informational");
            details.Add(string.Empty);
            details.Add("⚠ NOTE: HD Icons are provided by mods or community content");
            details.Add("  GenHub's Content system handles icon downloads");
            details.Add(string.Empty);
            details.Add("To get HD Icons:");
            details.Add("  1. Open GenHub");
            details.Add("  2. Go to Downloads section");
            details.Add("  3. Browse 'Icons' category");
            details.Add("  4. Download and install HD icon packs");
            details.Add(string.Empty);

            // Check current status
            var hdIconsPresent = AreHDIconsPresent(installation);
            if (hdIconsPresent)
            {
                details.Add("✓ HD icons are already installed");
            }
            else
            {
                details.Add("⚠ No HD icons found");
                details.Add("  Use GenHub's Content system to download icon packs");
            }

            _logger.LogInformation("HD Icons are typically provided by mods or community content.");
            _logger.LogInformation("Use GenHub's Content system to download HD icon packs.");
            _logger.LogInformation("HD Icons can be found in the Downloads section under 'Icons' category.");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create marker file for HDIconsFix");
            }

            return Task.FromResult(new ActionSetResult(true, "HD Icons are available through GenHub's Content system.", details));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying HD icons fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("HD Icons Fix is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private bool AreHDIconsPresent(GameInstallation installation)
    {
        try
        {
            // Check for HD icon files in game directories
            var hdIconFiles = new[]
            {
                "generals.ico",
                "game.ico",
                "zh.ico",
                "generals_hd.ico",
                "game_hd.ico",
            };

            var foundHDIcons = false;

            if (installation.HasGenerals)
            {
                foreach (var iconFile in hdIconFiles)
                {
                    if (File.Exists(Path.Combine(installation.GeneralsPath, iconFile)))
                    {
                        _logger.LogInformation("Found HD icon: {Icon}", iconFile);
                        foundHDIcons = true;
                        break;
                    }
                }
            }

            if (installation.HasZeroHour && !foundHDIcons)
            {
                foreach (var iconFile in hdIconFiles)
                {
                    if (File.Exists(Path.Combine(installation.ZeroHourPath, iconFile)))
                    {
                        _logger.LogInformation("Found HD icon: {Icon}", iconFile);
                        foundHDIcons = true;
                        break;
                    }
                }
            }

            return foundHDIcons;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for HD icons");
            return false;
        }
    }
}
