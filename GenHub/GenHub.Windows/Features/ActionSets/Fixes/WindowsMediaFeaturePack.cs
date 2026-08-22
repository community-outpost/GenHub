namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.IO;
using System.Linq;
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
    private readonly string _markerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GenHub", ActionSetConstants.Paths.SubActionSetMarkers, "WindowsMediaFeaturePack.done");

    /// <inheritdoc/>
    public override string Id => "WindowsMediaFeaturePack";

    /// <inheritdoc/>
    public override string Title => "Windows Media Feature Pack";

    /// <inheritdoc/>
    public override string Description => "Checks for Windows Media Feature Pack on Windows N editions to prevent video cutscene and audio crashes.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Windows N and KN editions lack essential media codecs required to play Generals and Zero Hour intro movies, campaign briefings, and background audio. This fix detects missing media components and guides you through enabling the Windows Media Feature Pack.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.Compatibility;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // Only applicable if Media Feature Pack is NOT installed (needs fixing)
        var mediaPackInstalled = IsMediaFeaturePackInstalled();
        return Task.FromResult(!mediaPackInstalled && (installation.HasGenerals || installation.HasZeroHour));
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        if (File.Exists(_markerPath)) return Task.FromResult(true);
        return Task.FromResult(IsMediaFeaturePackInstalled());
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken ct)
    {
        try
        {
            var mediaPackInstalled = IsMediaFeaturePackInstalled();

            if (mediaPackInstalled)
            {
                logger.LogInformation("Windows Media Feature Pack is already installed. No action needed.");
                return Task.FromResult(new ActionSetResult(true));
            }

            var osVersion = Environment.OSVersion.Version;
            var isWindows10OrLater = osVersion >= new Version(10, 0);

            if (!isWindows10OrLater)
            {
                logger.LogInformation("Windows Media Feature Pack is only available for Windows 10 and later. Your Windows version: {Version}", osVersion);
                return Task.FromResult(new ActionSetResult(true, null, ["Media Feature Pack not available for your Windows version."]));
            }

            logger.LogWarning("Windows Media Feature Pack is not installed. Please install it from Windows Settings > Optional features > Add a feature, or visit {Url}", ExternalUrls.WindowsMediaFeaturePackSupportUrl);

            try
            {
                var markerDir = Path.GetDirectoryName(_markerPath);
                if (!string.IsNullOrEmpty(markerDir))
                {
                    Directory.CreateDirectory(markerDir);
                }

                File.WriteAllText(_markerPath, DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create marker file.");
            }

            return Task.FromResult(new ActionSetResult(true, null, ["Please manually install Windows Media Feature Pack. See logs for details."]));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying Media Feature Pack fix");
            return Task.FromResult(new ActionSetResult(false, ex.Message));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken ct)
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
            logger.LogWarning(ex, "Failed to delete marker file.");
        }

        return Task.FromResult(new ActionSetResult(true, null, ["Media Feature Pack marker removed."]));
    }

    private bool IsMediaFeaturePackInstalled()
    {
        try
        {
            return HasMediaFeaturePackInRegistry() || HasWindowsMediaPlayer();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error checking for Media Feature Pack");
            return false;
        }
    }

    private bool HasMediaFeaturePackInRegistry()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(RegistryConstants.CbsPackagesKeyPath, false);
        if (key == null)
        {
            return false;
        }

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            if (!subKeyName.Contains("MediaFeaturePack", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var subKey = key.OpenSubKey(subKeyName, false);
            if (subKey != null && IsPackageInstalled(subKey))
            {
                logger.LogInformation("Found Media Feature Pack: {Package}", subKeyName);
                return true;
            }
        }

        return false;
    }

    private static bool IsPackageInstalled(Microsoft.Win32.RegistryKey subKey)
    {
        var installStateVal = subKey.GetValue(RegistryConstants.InstallStateValueName);
        if (installStateVal is int stateInt &&
            (stateInt == RegistryConstants.CbsInstallStateStaged ||
             stateInt == RegistryConstants.CbsInstallStateInstalled ||
             stateInt == RegistryConstants.CbsInstallStateSuperseded))
        {
            return true;
        }

        return installStateVal is string installState && installState.Equals("Installed", StringComparison.OrdinalIgnoreCase);
    }

    private bool HasWindowsMediaPlayer()
    {
        var wmpPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Windows Media Player",
            "wmplayer.exe");

        if (File.Exists(wmpPath))
        {
            logger.LogInformation("Found Windows Media Player: {Path}", wmpPath);
            return true;
        }

        return false;
    }
}
