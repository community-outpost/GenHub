namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that checks for Windows Media Feature Pack installation.
/// The Media Feature Pack is required for some media playback features in Windows N editions.
/// </summary>
public class WindowsMediaFeaturePack(ILogger<WindowsMediaFeaturePack> logger) : BaseActionSet(logger)
{
    private readonly ILogger<WindowsMediaFeaturePack> _logger = logger;
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "WindowsMediaFeaturePack.done");

    /// <inheritdoc/>
    public override string Id => "WindowsMediaFeaturePack";

    /// <inheritdoc/>
    public override string Title => "Windows Media Feature Pack";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Only applicable if Media Feature Pack is NOT installed (needs fixing)
        var mediaPackInstalled = IsMediaFeaturePackInstalled();
        return Task.FromResult(!mediaPackInstalled && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        if (File.Exists(_markerPath)) return Task.FromResult(true);
        return Task.FromResult(IsMediaFeaturePackInstalled());
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        try
        {
            var mediaPackInstalled = IsMediaFeaturePackInstalled();

            if (mediaPackInstalled)
            {
                _logger.LogInformation("Windows Media Feature Pack is already installed. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            // Check Windows version
            var osVersion = Environment.OSVersion.Version;
            var isWindows10OrLater = osVersion >= new Version(10, 0);

            if (!isWindows10OrLater)
            {
                _logger.LogInformation("Windows Media Feature Pack is only available for Windows 10 and later.");
                _logger.LogInformation("Your Windows version: {Version}", osVersion);
                return Task.FromResult(new ActionSetResult(true, "Media Feature Pack not available for your Windows version."));
            }

            // Provide guidance for installing Media Feature Pack
            _logger.LogWarning("Windows Media Feature Pack is not installed.");
            _logger.LogInformation("To install Windows Media Feature Pack:");
            _logger.LogInformation("1. Open Windows Settings");
            _logger.LogInformation("2. Go to 'Apps' > 'Optional features'");
            _logger.LogInformation("3. Click 'Add a feature'");
            _logger.LogInformation("4. Search for 'Media Feature Pack'");
            _logger.LogInformation("5. Click 'Install'");
            _logger.LogInformation(string.Empty);
            _logger.LogInformation("Alternatively, you can download it from Microsoft website:");
            _logger.LogInformation("https://support.microsoft.com/en-us/help/4033582/windows-media-feature-pack");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create marker file.");
            }

            return Task.FromResult(new ActionSetResult(true, "Please manually install Windows Media Feature Pack. See logs for details."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying Media Feature Pack fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Windows Media Feature Pack Fix is informational only. No undo action needed.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private bool IsMediaFeaturePackInstalled()
    {
        try
        {
            // Check for Media Feature Pack in registry
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages",
                false);

            if (key != null)
            {
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    if (subKeyName.Contains("MediaFeaturePack", StringComparison.OrdinalIgnoreCase))
                    {
                        using var subKey = key.OpenSubKey(subKeyName, false);
                        if (subKey != null)
                        {
                            var installState = subKey.GetValue("InstallState") as string;
                            if (installState == "Installed")
                            {
                                _logger.LogInformation("Found Media Feature Pack: {Package}", subKeyName);
                                return true;
                            }
                        }
                    }
                }
            }

            // Check for Windows Media Player
            var wmpPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Windows Media Player",
                "wmplayer.exe");

            if (File.Exists(wmpPath))
            {
                _logger.LogInformation("Found Windows Media Player: {Path}", wmpPath);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for Media Feature Pack");
            return false;
        }
    }
}
