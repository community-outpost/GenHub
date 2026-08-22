namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that ensures Arial font is available for the game.
/// Generals and Zero Hour require Arial font for proper text rendering.
/// </summary>
public class GenArial(ILogger<GenArial> logger) : BaseActionSet(logger)
{
    private static readonly IReadOnlyList<string> ArialFiles =
    [
        "arial.ttf",
        "arialbd.ttf",
        "ariali.ttf",
        "arialbi.ttf",
        "ARIAL.TTF",
    ];

    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "GenArial.done");

    /// <inheritdoc/>
    public override string Id => "GenArial";

    /// <inheritdoc/>
    public override string Title => "Arial Font";

    /// <inheritdoc/>
    public override string Description => "Verifies standard TrueType Arial fonts are installed so all in-game menus, HUD, and chat text render properly.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Generals and Zero Hour depend on standard TrueType Arial fonts to render in-game menus, UI buttons, and chat overlays. On streamlined or modified Windows editions lacking standard fonts, in-game text can render as invisible or corrupted boxes. This fix checks font availability and guides installation if needed.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.Compatibility;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // Only applicable if Arial is NOT installed (needs to be fixed)
        var arialInstalled = IsArialFontInstalled();
        return Task.FromResult(!arialInstalled && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        if (MarkerExists(_markerPath)) return Task.FromResult(true);
        return Task.FromResult(IsArialFontInstalled());
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        try
        {
            var arialInstalled = IsArialFontInstalled();

            if (arialInstalled)
            {
                logger.LogInformation("Arial font is already installed. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            // Provide guidance for installing Arial font
            logger.LogWarning("Arial font is not installed. This may cause text rendering issues. Please install Arial from Windows Settings > Optional features > Add a font.");

            WriteMarkerFile(_markerPath);

            return Task.FromResult(new ActionSetResult(true, null, ["Please manually install Arial font. See logs for details."]));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying Arial font fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        DeleteMarkerFile(_markerPath);
        return Task.FromResult(new ActionSetResult(true, null, ["Arial font marker removed."]));
    }

    private bool IsArialFontInstalled()
    {
        try
        {
            // Check for Arial font in Windows fonts directory
            var fontsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Fonts");

            var existingFont = ArialFiles.FirstOrDefault(fontFile => File.Exists(Path.Combine(fontsPath, fontFile)));
            if (existingFont != null)
            {
                logger.LogInformation("Found Arial font: {Font}", existingFont);
                return true;
            }

            // Check for Arial in registry
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                RegistryConstants.FontsKeyPath,
                false);

            if (key != null)
            {
                if (key.GetValue(RegistryConstants.ArialFontValueName) != null)
                {
                    logger.LogInformation("Found Arial font in registry: {Font}", RegistryConstants.ArialFontValueName);
                    return true;
                }

                var fontValueName = key.GetValueNames().FirstOrDefault(v => v.Contains("Arial", StringComparison.OrdinalIgnoreCase));
                if (fontValueName != null)
                {
                    logger.LogInformation("Found Arial font in registry: {Font}", fontValueName);
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking for Arial font");
            return false;
        }
    }
}
