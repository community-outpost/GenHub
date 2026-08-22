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
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "IntelGfxDriverCompatibility.done");

    /// <inheritdoc/>
    public override string Id => "IntelGfxDriverCompatibility";

    /// <inheritdoc/>
    public override string Title => "Intel Graphics Driver Compatibility";

    /// <inheritdoc/>
    public override string Description => "Detects Intel integrated/discrete GPUs and guides updating drivers to prevent black screens and texture corruption.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Older DirectX 8 titles frequently encounter rendering anomalies, flashing water shaders, or black-screen crashes on Intel integrated and Arc graphics. This fix identifies Intel display adapters and guides installing the latest driver revisions to maintain rendering stability.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.Compatibility;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // Only applicable if Intel graphics are present
        var hasIntelGfx = HasIntelGraphics();
        return Task.FromResult(hasIntelGfx && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
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
            logger.LogError(ex, "Error checking Intel graphics driver status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        try
        {
            var hasIntelGfx = HasIntelGraphics();

            if (!hasIntelGfx)
            {
                logger.LogInformation("Intel graphics not detected. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            if (IsIntelDriverUpToDate())
            {
                logger.LogInformation("Intel graphics driver is up to date. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            logger.LogWarning("Intel graphics driver detected. May need update from Intel website: {Url}", ExternalUrls.IntelDriverDownloadUrl);

            WriteMarkerFile(_markerPath);

            return Task.FromResult(new ActionSetResult(true, null, ["Please update Intel graphics driver. See logs for details."]));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying Intel graphics driver compatibility fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        DeleteMarkerFile(_markerPath);
        return Task.FromResult(new ActionSetResult(true, null, ["Intel graphics marker removed."]));
    }

    private bool HasIntelGraphics()
    {
        try
        {
            // Check for Intel graphics in system
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"{RegistryConstants.IntelGraphicsClassKeyPath}\0000",
                false);

            if (key?.GetValue("DriverDesc") is string driverDesc && driverDesc.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Found Intel graphics: {Driver}", driverDesc);
                return true;
            }

            // Check for Intel graphics via WMI
            using var searcher = new ManagementObjectSearcher(RegistryConstants.WmiScopeCimV2, RegistryConstants.WmiQueryVideoController);
            using var results = searcher.Get();

            foreach (ManagementBaseObject result in results)
            {
                using (result)
                {
                    if (result["Name"] is string name && name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogInformation("Found Intel graphics via WMI: {Name}", name);
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking for Intel graphics");
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

            if (key?.GetValue("Version") is string version)
            {
                logger.LogInformation("Intel Driver & Support Assistant version: {Version}", version);

                // Assume recent version means driver is reasonably up to date
                return true;
            }

            // If we can't determine, assume it needs checking
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking Intel driver version");
            return false;
        }
    }
}
