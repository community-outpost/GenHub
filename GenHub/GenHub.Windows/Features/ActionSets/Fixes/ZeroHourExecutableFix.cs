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
/// Fix that ensures that Zero Hour executable is properly patched.
/// This fix checks if that official 1.04 patch has been applied.
/// </summary>
public class ZeroHourExecutableFix(ILogger<ZeroHourExecutableFix> logger) : BaseActionSet(logger)
{
    private readonly ILogger<ZeroHourExecutableFix> _logger = logger;

    /// <inheritdoc/>
    public override string Id => "ZeroHourExecutableFix";

    /// <inheritdoc/>
    public override string Title => "Zero Hour Executable Fix";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation)
    {
        // User requested to disable this fix as it is handled by the Downloads tab
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation)
    {
        try
        {
            if (!installation.HasZeroHour)
            {
                return Task.FromResult(false);
            }

            var gameExePath = Path.Combine(installation.ZeroHourPath, ActionSetConstants.FileNames.GameExe);
            if (!File.Exists(gameExePath))
            {
                return Task.FromResult(false);
            }

            // Check file version to verify it's 1.04
            var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(gameExePath);
            var version = versionInfo.FileVersion;

            // 1.04 version should be 1.4.0.0 or similar
            if (version != null && version.StartsWith("1.4"))
            {
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Zero Hour executable version");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> ApplyInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        var details = new List<string>();

        try
        {
            if (!installation.HasZeroHour)
            {
                details.Add("✗ Zero Hour is not installed");
                return Task.FromResult(new ActionSetResult(false, "Zero Hour is not installed in this installation.", details));
            }

            details.Add("Zero Hour Executable Fix - Informational");
            details.Add(string.Empty);
            details.Add("This fix ensures the Zero Hour 1.04 patch is applied.");
            details.Add("Note: Automatic patching is currently disabled. Please use the Downloads section.");
            details.Add(string.Empty);

            var gameExePath = Path.Combine(installation.ZeroHourPath, ActionSetConstants.FileNames.GameExe);

            if (File.Exists(gameExePath))
            {
                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(gameExePath);
                var version = versionInfo.FileVersion;

                details.Add($"Current executable: {Path.GetFileName(gameExePath)}");
                details.Add($"Current version: {version ?? "unknown"}");

                if (version != null && version.StartsWith("1.4"))
                {
                    details.Add("✓ Zero Hour 1.04 patch is already applied");
                }
                else
                {
                    details.Add("⚠ Zero Hour 1.04 patch needs to be applied");
                    details.Add("  Please use the 'Downloads' section in GenHub to get the 1.04 patch.");
                }
            }
            else
            {
                details.Add("⚠ Zero Hour executable not found");
                details.Add($"  Expected location: {gameExePath}");
            }

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying ZeroHourExecutableFix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Undoing Zero Hour Executable Fix is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }
}
