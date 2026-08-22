namespace GenHub.Windows.Features.ActionSets.Fixes;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;

/// <summary>
/// Fix that ensures that Generals executable is properly patched.
/// This fix checks if the official 1.08 patch has been applied.
/// </summary>
public class VanillaExecutableFix(ILogger<VanillaExecutableFix> logger) : BaseExecutableVersionFix(logger)
{
    /// <inheritdoc/>
    public override string Id => "VanillaExecutableFix";

    /// <inheritdoc/>
    public override string Title => "Generals 1.08 Version Check";

    /// <inheritdoc/>
    public override string Description => "Verifies that the Generals game client executable is updated to official version 1.08.";

    /// <inheritdoc/>
    public override string DetailedDescription => "Running an unpatched version of Generals causes multiplayer version mismatches and crashes. This diagnostic verifies that your base game executable is present and updated to official version 1.08. If outdated, use the Downloads section or Patch 1.08 to update your game client.";

    /// <inheritdoc/>
    public override string Category => ActionSetConstants.Categories.Compatibility;

    /// <inheritdoc/>
    public override bool IsCoreFix => false;

    /// <inheritdoc/>
    public override bool IsCrucialFix => false;

    /// <inheritdoc/>
    protected override string GameDisplayName => "Generals";

    /// <inheritdoc/>
    protected override string TargetVersionDisplay => "1.08";

    /// <inheritdoc/>
    protected override IReadOnlyList<string> VersionPrefixes => ["1.8", "1.08"];

    /// <inheritdoc/>
    protected override IReadOnlyList<string> CandidateExecutableNames => [ActionSetConstants.FileNames.GeneralsExe];

    /// <inheritdoc/>
    public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default)
    {
        return Task.FromResult(installation.HasGenerals);
    }

    /// <inheritdoc/>
    protected override bool HasGame(GameInstallation installation) => installation.HasGenerals;

    /// <inheritdoc/>
    protected override string? GetGamePath(GameInstallation installation) => installation.GeneralsPath;
}
