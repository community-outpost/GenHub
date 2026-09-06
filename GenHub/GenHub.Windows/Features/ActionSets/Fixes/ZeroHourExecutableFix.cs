namespace GenHub.Windows.Features.ActionSets.Fixes;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that ensures that Zero Hour executable is properly patched.
/// This fix checks if that official 1.04 patch has been applied.
/// </summary>
public class ZeroHourExecutableFix(ILogger<ZeroHourExecutableFix> logger) : BaseExecutableVersionFix(logger)
{
    private static readonly IReadOnlyList<string> CandidateExes =
    [
        ActionSetConstants.FileNames.GeneralsExe,
        ActionSetConstants.FileNames.GameExe,
    ];

    /// <inheritdoc/>
    public override string Id => "ZeroHourExecutableFix";

    /// <inheritdoc/>
    public override string Title => "Zero Hour 1.04 Version Check";

    /// <inheritdoc/>
    public override string Description => "Verifies that the Zero Hour game client executable is updated to official version 1.04.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Zero Hour requires official executable version 1.04 to support multiplayer, GenTool, and modern community mods. This diagnostic validates your game executables. If outdated, use the Downloads section or Patch 1.04 to update your game client.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.CoreAndStability;

    /// <inheritdoc/>
    public override bool IsCoreFix => true;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    protected override string GameDisplayName => "Zero Hour";

    /// <inheritdoc/>
    protected override string TargetVersionDisplay => "1.04";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> VersionPrefixes => ["1.4", "1.04"];

    /// <inheritdoc/>
    protected override IReadOnlyList<string> CandidateExecutableNames => CandidateExes;

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        // User requested to disable this fix as it is handled by the Downloads tab
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    protected override bool HasGame(GameInstallation installation) => installation.HasZeroHour;

    /// <inheritdoc/>
    protected override string? GetGamePath(GameInstallation installation) => installation.ZeroHourPath;
}
