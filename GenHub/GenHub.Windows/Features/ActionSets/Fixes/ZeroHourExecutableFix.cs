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
/// Fix that ensures that Zero Hour executable is properly patched.
/// This fix checks if that official 1.04 patch has been applied.
/// </summary>
public class ZeroHourExecutableFix(ILogger<ZeroHourExecutableFix> logger) : BaseActionSet(logger)
{
    private static readonly IReadOnlyList<string> CandidateExes =
    [
        ActionSetConstants.FileNames.GeneralsExe,
        ActionSetConstants.FileNames.GameDat,
        ActionSetConstants.FileNames.GameExe,
    ];

    /// <inheritdoc/>
    public override string Id => "ZeroHourExecutableFix";

    /// <inheritdoc/>
    public override string Title => "Zero Hour Executable Fix";

    /// <inheritdoc/>
    public override string Description => "Verifies that the Zero Hour game executable is present and updated to official version 1.04.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Zero Hour requires official executable version 1.04 to support online multiplayer, GenTool, and modern community mods. This check validates your game executables and ensures your installation is ready for competitive play.";

    /// <inheritdoc/>
    public override string Category => "Core & Stability";

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => true;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // User requested to disable this fix as it is handled by the Downloads tab
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default)
    {
        try
        {
            if (!installation.HasZeroHour)
            {
                return Task.FromResult(false);
            }

            var gameExePath = FindExecutable(installation.ZeroHourPath);
            if (gameExePath == null)
            {
                return Task.FromResult(false);
            }

            // Check file version to verify it's 1.04
            var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(gameExePath);
            var version = versionInfo.FileVersion;

            // 1.04 version should be 1.4.0.0 or similar
            if (version?.StartsWith("1.4") == true)
            {
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking Zero Hour executable version");
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

            var gameExePath = FindExecutable(installation.ZeroHourPath);

            if (gameExePath != null)
            {
                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(gameExePath);
                var version = versionInfo.FileVersion;

                details.Add($"Current executable: {Path.GetFileName(gameExePath)}");
                details.Add($"Current version: {version ?? "unknown"}");

                if (version?.StartsWith("1.4") == true)
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
                details.Add($"  Expected location in: {installation.ZeroHourPath}");
            }

            return Task.FromResult(new ActionSetResult(true, null, details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error applying ZeroHourExecutableFix");
            details.Add($"✗ Error: {ex.Message}");
            return Task.FromResult(new ActionSetResult(false, ex.Message, details));
        }
    }

    /// <inheritdoc/>
    protected override Task<ActionSetResult> UndoInternalAsync(GameInstallation installation, CancellationToken cancellationToken)
    {
        logger.LogWarning("Undoing Zero Hour Executable Fix is not supported via GenHub.");
        return Task.FromResult(new ActionSetResult(true));
    }

    private static string? FindExecutable(string zeroHourPath)
    {
        foreach (var exeName in CandidateExes)
        {
            var p = Path.Combine(zeroHourPath, exeName);
            if (File.Exists(p)) return p;
        }

        return null;
    }
}
