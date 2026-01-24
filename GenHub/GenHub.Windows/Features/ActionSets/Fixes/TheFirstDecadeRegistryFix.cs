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
    private readonly IRegistryService _registryService = registryService;
    private readonly ILogger<TheFirstDecadeRegistryFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "TheFirstDecadeRegistryFix";

    /// <inheritdoc/>
    public override string Title => "The First Decade Registry";

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
            // Check if TFD registry entries exist
            var tfdInstalled = _registryService.GetStringValue(
                RegistryConstants.TheFirstDecadeKeyPath,
                RegistryConstants.InstallPathValueName);

            return Task.FromResult(!string.IsNullOrEmpty(tfdInstalled));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking TFD registry status");
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
                _logger.LogWarning("Could not determine TFD installation path");
                return Task.FromResult(new ActionSetResult(false, "Could not determine TFD installation path", details));
            }

            details.Add($"✓ Detected TFD path: {tfdPath}");
            details.Add("Creating TFD registry entries...");

            // Create TFD registry entries
            _registryService.SetStringValue(
                RegistryConstants.TheFirstDecadeKeyPath,
                RegistryConstants.InstallPathValueName,
                tfdPath);

            _registryService.SetStringValue(
                RegistryConstants.TheFirstDecadeKeyPath,
                RegistryConstants.VersionValueName,
                RegistryConstants.TfdVersionValue);

            details.Add("✓ Created: HKCU\\SOFTWARE\\EA Games\\Command & Conquer The First Decade");
            details.Add($"  • InstallPath = {tfdPath}");
            details.Add($"  • Version = {RegistryConstants.TfdVersionValue}");
            details.Add("✓ The First Decade registry configuration completed successfully");

            _logger.LogInformation("Successfully created TFD registry entries at {Path} with {Count} actions", tfdPath, details.Count);

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying TFD registry fix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Undoing TFD Registry Fix is not recommended as it may break game detection.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private string? FindTFDPath(string gamePath)
    {
        try
        {
            var directory = new DirectoryInfo(gamePath);

            // Check if we're already in a TFD structure
            // TFD typically has structure: TFD\Command & Conquer Generals\...
            if (directory.Parent?.Parent?.Name.Equals("Command & Conquer The First Decade", StringComparison.OrdinalIgnoreCase) == true)
            {
                return directory.Parent.Parent.FullName;
            }

            // Check if parent is "Command & Conquer Generals" or similar
            if (directory.Parent?.Name.Contains("Generals", StringComparison.OrdinalIgnoreCase) == true)
            {
                // Check if grandparent is TFD
                if (directory.Parent.Parent?.Name.Contains("First Decade", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return directory.Parent.Parent.FullName;
                }
            }

            // Default to current path if we can't determine TFD structure
            return gamePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error finding TFD path");
            return null;
        }
    }
}
