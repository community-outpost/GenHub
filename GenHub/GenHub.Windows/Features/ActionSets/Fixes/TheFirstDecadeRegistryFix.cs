namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Infrastructure;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that creates registry entries for The First Decade (TFD) version detection.
/// This ensures the game can properly detect if it's running from TFD installation.
/// </summary>
public class TheFirstDecadeRegistryFix(
    IRegistryService registryService,
    ILogger<TheFirstDecadeRegistryFix> logger) : BaseActionSet(logger)
{
    /// <inheritdoc/>
    public override string Id => "TheFirstDecadeRegistryFix";

    /// <inheritdoc/>
    public override string Title => "The First Decade Registry";

    /// <inheritdoc/>
    public override string Description => "Restores missing \"The First Decade\" registry keys required for proper game detection and patch installation.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Command & Conquer: The First Decade compilation installs rely on central registry keys to link Generals and Zero Hour to official patches and tools. This fix locates your TFD base folder and rebuilds the required registry entries so expansions recognize your installation.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.Compatibility;

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
            // Check if TFD registry entries exist
            var tfdInstalled = registryService.GetStringValue(
                RegistryConstants.TheFirstDecadeKeyPath,
                RegistryConstants.InstallPathValueName);

            return Task.FromResult(!string.IsNullOrEmpty(tfdInstalled));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking TFD registry status");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            details.Add("Starting The First Decade registry configuration...");

            // Determine the base installation path
            string basePath = installation.HasGenerals
                ? installation.GeneralsPath
                : installation.ZeroHourPath;

            details.Add($"Detecting TFD installation path from: {basePath}");

            // Navigate up to find the TFD base directory
            var tfdPath = FindTFDPath(basePath);
            if (string.IsNullOrEmpty(tfdPath))
            {
                details.Add("✗ Could not determine TFD installation path");
                details.Add("  Game may not be installed as part of The First Decade");
                logger.LogWarning("Could not determine TFD installation path");
                return Task.FromResult(new ActionSetResult(false, "Could not determine TFD installation path", details));
            }

            details.Add($"✓ Detected TFD path: {tfdPath}");
            details.Add("Creating TFD registry entries...");

            // Create TFD registry entries
            var s1 = registryService.SetStringValue(
                RegistryConstants.TheFirstDecadeKeyPath,
                RegistryConstants.InstallPathValueName,
                tfdPath);

            var s2 = registryService.SetStringValue(
                RegistryConstants.TheFirstDecadeKeyPath,
                RegistryConstants.VersionValueName,
                RegistryConstants.TfdVersionData);

            if (!s1 || !s2)
            {
                details.Add("✗ Failed to write The First Decade registry entries (permissions?)");
                return Task.FromResult(new ActionSetResult(false, "Failed to write The First Decade registry entries", details));
            }

            details.Add($"✓ Created: HKLM\\{RegistryConstants.TheFirstDecadeKeyPath}");
            details.Add($"  • InstallPath = {tfdPath}");
            details.Add($"  • Version = {RegistryConstants.TfdVersionData}");
            details.Add("✓ The First Decade registry configuration completed successfully");

            logger.LogInformation("Successfully created TFD registry entries at {Path} with {Count} actions", tfdPath, details.Count);

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying TFD registry fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Undoing TFD Registry Fix is not recommended as it may break game detection.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private string? FindTFDPath(string gamePath)
    {
        try
        {
            var directory = new DirectoryInfo(gamePath);

            // Direct parent is TFD (e.g. C:\TFD\Command & Conquer Generals Zero Hour)
            if (directory.Parent?.Name.Contains("The First Decade", StringComparison.OrdinalIgnoreCase) == true ||
                directory.Parent?.Name.Contains("First Decade", StringComparison.OrdinalIgnoreCase) == true)
            {
                return directory.Parent.FullName;
            }

            // Grandparent is TFD (e.g. C:\TFD\Command & Conquer Generals\...)
            if (directory.Parent?.Parent?.Name.Contains("The First Decade", StringComparison.OrdinalIgnoreCase) == true ||
                directory.Parent?.Parent?.Name.Contains("First Decade", StringComparison.OrdinalIgnoreCase) == true)
            {
                return directory.Parent.Parent.FullName;
            }

            return directory.Parent?.FullName ?? gamePath;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error finding TFD path");
            return null;
        }
    }
}
