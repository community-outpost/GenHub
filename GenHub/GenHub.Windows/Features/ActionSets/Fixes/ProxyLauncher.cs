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
/// Fix that provides information about proxy-based launching.
/// This fix explains the proxy launcher system used by GenHub.
/// </summary>
public class ProxyLauncher(ILogger<ProxyLauncher> logger) : BaseActionSet(logger)
{
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "ProxyLauncher.done");

    /// <inheritdoc/>
    public override string Id => "ProxyLauncher";

    /// <inheritdoc/>
    public override string Title => "Proxy Launcher";

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
            logger.LogError(ex, "Error checking proxy launcher status");
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
                "Proxy Launcher Information:",
                "GenHub uses a proxy launcher system for game execution.",
                "Benefits of Proxy Launcher:",
                "- Improved compatibility with modern Windows versions",
                "- Better process isolation",
                "- Enhanced error handling and logging",
                "- Support for custom launch parameters",
                "- Integration with GenHub's ActionSet framework",
                "The proxy launcher is automatically used when launching games through GenHub.",
                "No manual configuration is required.",
            };

            var dir = Path.GetDirectoryName(_markerPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_markerPath, DateTime.UtcNow.ToString("O"));

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying proxy launcher fix");
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
            logger.LogWarning(ex, "Failed to delete marker file for ProxyLauncher");
        }

        return Task.FromResult(new ActionSetResult(true, null, ["Proxy launcher marker removed."]));
    }
}
