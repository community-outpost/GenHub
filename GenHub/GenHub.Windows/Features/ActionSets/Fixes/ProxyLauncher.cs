namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that provides information about proxy-based launching.
/// This fix explains the proxy launcher system used by GenHub.
/// </summary>
public class ProxyLauncher(ILogger<ProxyLauncher> logger) : BaseActionSet(logger)
{
    private readonly ILogger<ProxyLauncher> _logger = logger;
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", "sub_markers", "ProxyLauncher.done");

    /// <inheritdoc/>
    public override string Id => "ProxyLauncher";

    /// <inheritdoc/>
    public override string Title => "Proxy Launcher";

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
            _logger.LogError(ex, "Error checking proxy launcher status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            // Provide information about proxy launcher
            _logger.LogInformation("Proxy Launcher Information:");
            _logger.LogInformation("GenHub uses a proxy launcher system for game execution.");
            _logger.LogInformation(string.Empty);
            _logger.LogInformation("Benefits of Proxy Launcher:");
            _logger.LogInformation("- Improved compatibility with modern Windows versions");
            _logger.LogInformation("- Better process isolation");
            _logger.LogInformation("- Enhanced error handling and logging");
            _logger.LogInformation("- Support for custom launch parameters");
            _logger.LogInformation("- Integration with GenHub's ActionSet framework");
            _logger.LogInformation(string.Empty);
            _logger.LogInformation("The proxy launcher is automatically used when launching games through GenHub.");
            _logger.LogInformation("No manual configuration is required.");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create marker file for ProxyLauncher");
            }

            return Task.FromResult(new ActionSetResult(true, "Proxy launcher is built into GenHub and automatically used."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying proxy launcher fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Proxy Launcher Fix is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
