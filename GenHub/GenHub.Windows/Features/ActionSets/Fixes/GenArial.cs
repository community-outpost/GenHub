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
/// Fix that ensures Arial font is available for the game.
/// Generals and Zero Hour require Arial font for proper text rendering.
/// </summary>
public class GenArial(ILogger<GenArial> logger) : BaseActionSet(logger)
{
    private readonly ILogger<GenArial> _logger = logger;
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "GenArial.done");

    /// <inheritdoc/>
    public override string Id => "GenArial";

    /// <inheritdoc/>
    public override string Title => "Arial Font";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Only applicable if Arial is NOT installed (needs to be fixed)
        var arialInstalled = IsArialFontInstalled();
        return Task.FromResult(!arialInstalled && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        if (File.Exists(_markerPath)) return Task.FromResult(true);
        return Task.FromResult(IsArialFontInstalled());
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            var arialInstalled = IsArialFontInstalled();

            if (arialInstalled)
            {
                _logger.LogInformation("Arial font is already installed. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            // Provide guidance for installing Arial font
            _logger.LogWarning("Arial font is not installed. This may cause text rendering issues.");
            _logger.LogInformation("Arial font is typically included with Windows.");
            _logger.LogInformation("To install Arial font:");
            _logger.LogInformation("1. Open Windows Settings");
            _logger.LogInformation("2. Go to 'Apps' > 'Optional features'");
            _logger.LogInformation("3. Click 'View features' next to 'Add a font'");
            _logger.LogInformation("4. Click 'Get more fonts in Microsoft Store'");
            _logger.LogInformation("5. Search for 'Arial' and install");
            _logger.LogInformation(string.Empty);
            _logger.LogInformation("Alternatively, you can:");
            _logger.LogInformation("- Copy Arial font files from another Windows computer");
            _logger.LogInformation("- Download Arial font from a trusted source");
            _logger.LogInformation("- Install the font by right-clicking and selecting 'Install for all users'");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create marker file.");
            }

            return Task.FromResult(new ActionSetResult(true, "Please manually install Arial font. See logs for details."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying Arial font fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("GenArial Fix is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private bool IsArialFontInstalled()
    {
        try
        {
            // Check for Arial font in Windows fonts directory
            var fontsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Fonts");

            var arialFiles = new[]
            {
                "arial.ttf",
                "arialbd.ttf",
                "ariali.ttf",
                "arialbi.ttf",
                "ARIAL.TTF",
            };

            foreach (var fontFile in arialFiles)
            {
                if (File.Exists(Path.Combine(fontsPath, fontFile)))
                {
                    _logger.LogInformation("Found Arial font: {Font}", fontFile);
                    return true;
                }
            }

            // Check for Arial in registry
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts",
                false);

            if (key != null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    if (valueName.Contains("Arial", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogInformation("Found Arial font in registry: {Font}", valueName);
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for Arial font");
            return false;
        }
    }
}
