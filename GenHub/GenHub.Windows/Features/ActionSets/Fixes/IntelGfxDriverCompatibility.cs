namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that provides Intel graphics driver compatibility guidance.
/// Intel graphics drivers may have compatibility issues with older DirectX games.
/// </summary>
public class IntelGfxDriverCompatibility(ILogger<IntelGfxDriverCompatibility> logger) : BaseActionSet(logger)
{
    private readonly ILogger<IntelGfxDriverCompatibility> _logger = logger;
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "IntelGfxDriverCompatibility.done");

    /// <inheritdoc/>
    public override string Id => "IntelGfxDriverCompatibility";

    /// <inheritdoc/>
    public override string Title => "Intel Graphics Driver Compatibility";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Only applicable if Intel graphics are present
        var hasIntelGfx = HasIntelGraphics();
        return Task.FromResult(hasIntelGfx && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            // Check if Intel graphics is present
            var hasIntelGfx = HasIntelGraphics();

            if (!hasIntelGfx)
            {
                // If Intel graphics is not present, it's not applicable
                return Task.FromResult(false);
            }

            if (File.Exists(_markerPath)) return Task.FromResult(true);

            // Check if Intel graphics driver is up to date
            var driverUpToDate = IsIntelDriverUpToDate();

            return Task.FromResult(driverUpToDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Intel graphics driver status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            var hasIntelGfx = HasIntelGraphics();

            if (!hasIntelGfx)
            {
                _logger.LogInformation("Intel graphics not detected. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            // Check if driver is up to date
            if (IsIntelDriverUpToDate())
            {
                _logger.LogInformation("Intel graphics driver is up to date. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            // Provide guidance for Intel graphics driver
            _logger.LogWarning("Intel graphics driver detected. May need update for best compatibility.");
            _logger.LogInformation("To update Intel graphics driver:");
            _logger.LogInformation("1. Open Intel Driver & Support Assistant");
            _logger.LogInformation("2. Go to 'Drivers' tab");
            _logger.LogInformation("3. Click 'Check for updates'");
            _logger.LogInformation("4. Follow prompts to install latest driver");
            _logger.LogInformation(string.Empty);
            _logger.LogInformation("Alternatively, download from Intel website:");
            _logger.LogInformation("{Url}", ExternalUrls.IntelDriverDownloadUrl);
            _logger.LogInformation(string.Empty);
            _logger.LogInformation("Note: After updating driver, you may need to:");
            _logger.LogInformation("- Restart your computer");
            _logger.LogInformation("- Run GenHub fixes again");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create marker file for IntelGfxDriverCompatibility");
            }

            _logger.LogInformation("- Test game performance");

            return Task.FromResult(new ActionSetResult(true, "Please update Intel graphics driver. See logs for details."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying Intel graphics driver compatibility fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Intel Graphics Driver Compatibility Fix is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private bool HasIntelGraphics()
    {
        try
        {
            // Check for Intel graphics in system
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"{RegistryConstants.IntelGraphicsClassKeyPath}\0000",
                false);

            if (key != null)
            {
                if (key.GetValue("DriverDesc") is string driverDesc && driverDesc.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Found Intel graphics: {Driver}", driverDesc);
                    return true;
                }
            }

            // Check for Intel graphics via WMI
            using var searcher = new ManagementObjectSearcher("root\\CIMV2", "SELECT * FROM Win32_VideoController");
            using var results = searcher.Get();

            foreach (ManagementBaseObject result in results)
            {
                if (result["Name"] is string name && name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Found Intel graphics via WMI: {Name}", name);
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for Intel graphics");
            return false;
        }
    }

    private bool IsIntelDriverUpToDate()
    {
        try
        {
            // This is a simplified check - actual driver version checking is complex
            // We'll check if Intel Driver & Support Assistant is installed
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                RegistryConstants.IntelMEWizKeyPath,
                false);

            if (key != null)
            {
                if (key.GetValue("Version") is string version)
                {
                    _logger.LogInformation("Intel Driver & Support Assistant version: {Version}", version);

                    // Assume recent version means driver is reasonably up to date
                    return true;
                }
            }

            // If we can't determine, assume it needs checking
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking Intel driver version");
            return false;
        }
    }
}
