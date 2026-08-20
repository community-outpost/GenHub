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
/// Fix that ensures that Generals executable is properly patched.
/// This fix checks if the official 1.08 patch has been applied.
/// </summary>
public class VanillaExecutableFix(ILogger<VanillaExecutableFix> logger) : BaseActionSet(logger)
{
    /// <inheritdoc/>
    public override string Id => "VanillaExecutableFix";

    /// <inheritdoc/>
    public override string Title => "Generals Executable Fix";

    /// <inheritdoc/>
    public override string Description => "Verifies that the Generals executable is properly installed and updated to official version 1.08.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Running an unpatched version of the base Generals executable causes multiplayer version mismatch errors and mod incompatibilities. This check verifies that your base game executable is present, healthy, and updated to official version 1.08.";

    /// <inheritdoc/>
    public override string Category => "Compatibility";

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // Only applicable for Generals installations
        return Task.FromResult(installation.HasGenerals);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            if (!installation.HasGenerals)
            {
                return Task.FromResult(false);
            }

            var generalsExePath = Path.Combine(installation.GeneralsPath, ActionSetConstants.FileNames.GeneralsExe);
            if (!File.Exists(generalsExePath))
            {
                return Task.FromResult(false);
            }

            // Check file version to verify it's 1.08
            var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(generalsExePath);
            var version = versionInfo.FileVersion;

            // 1.08 version should be 1.8.0.0 or similar
            if (version?.StartsWith("1.8") == true)
            {
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking Generals executable version");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            if (!installation.HasGenerals)
            {
                details.Add("✗ Generals is not installed");
                return Task.FromResult(new ActionSetResult(false, "Generals is not installed in this installation.", details));
            }

            details.Add("Generals Executable Fix - Informational");
            details.Add(string.Empty);
            details.Add("This fix ensures the Generals 1.08 patch is applied.");
            details.Add("The actual patching is done by the 'Generals 1.08 Patch' fix.");
            details.Add(string.Empty);

            var generalsExePath = Path.Combine(installation.GeneralsPath, ActionSetConstants.FileNames.GeneralsExe);
            if (File.Exists(generalsExePath))
            {
                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(generalsExePath);
                var version = versionInfo.FileVersion;

                details.Add($"Current executable: {Path.GetFileName(generalsExePath)}");
                details.Add($"Current version: {version ?? "unknown"}");

                if (version?.StartsWith("1.8") == true)
                {
                    details.Add("✓ Generals 1.08 patch is already applied");
                    return Task.FromResult(new ActionSetResult(true, null, details));
                }

                details.Add("⚠ Generals 1.08 patch needs to be applied");
                details.Add("  Please apply the 'Generals 1.08 Patch' fix");
                return Task.FromResult(new ActionSetResult(false, "Generals executable is not version 1.08. Please apply Patch108Fix.", details));
            }

            details.Add("⚠ Generals executable not found");
            details.Add($"  Expected location: {generalsExePath}");
            return Task.FromResult(new ActionSetResult(false, $"Generals executable not found at {generalsExePath}", details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying VanillaExecutableFix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Undoing Generals Executable Fix is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
