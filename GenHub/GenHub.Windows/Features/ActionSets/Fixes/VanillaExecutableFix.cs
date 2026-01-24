namespace GenHub.Windows.Features.ActionSets.Fixes;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that ensures that Generals executable is properly patched.
/// This fix checks if the official 1.08 patch has been applied.
/// </summary>
public class VanillaExecutableFix(ILogger<VanillaExecutableFix> logger) : BaseActionSet(logger)
{
    private readonly ILogger<VanillaExecutableFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "VanillaExecutableFix";

    /// <inheritdoc/>
    public override string Title => "Generals Executable Fix";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // Only applicable for Generals installations
        return Task.FromResult(installation.HasGenerals);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
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
            if (version != null && version.StartsWith("1.8"))
            {
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Generals executable version");
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

                if (version != null && version.StartsWith("1.8"))
                {
                    details.Add("✓ Generals 1.08 patch is already applied");
                }
                else
                {
                    details.Add("⚠ Generals 1.08 patch needs to be applied");
                    details.Add("  Please apply the 'Generals 1.08 Patch' fix");
                }
            }
            else
            {
                details.Add("⚠ Generals executable not found");
                details.Add($"  Expected location: {generalsExePath}");
            }

            _logger.LogInformation("VanillaExecutableFix ensures Generals 1.08 patch is applied via Patch108Fix.");

            // This fix is a wrapper that ensures that the official patch is applied.
            // The actual patching is done by Patch108Fix.
            // This fix exists for compatibility with GenPatcher's fix structure.
            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying VanillaExecutableFix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Undoing Generals Executable Fix is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
