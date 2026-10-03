using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using System.Collections.Generic;

namespace GenHub.Features.Content.Services.Reconciliation;

/// <summary>
/// Execution arguments for publisher update strategies.
/// </summary>
/// <param name="Strategy">The update strategy selected.</param>
/// <param name="OldManifests">List of older content manifests being superseded.</param>
/// <param name="NewManifests">List of newly acquired content manifests.</param>
/// <param name="ManifestMapping">Mapping from old manifest IDs to new manifest IDs.</param>
/// <param name="LatestVersion">The latest version string.</param>
/// <param name="ShouldDeleteOldVersions">Whether old manifest versions should be deleted.</param>
/// <param name="TriggeringProfileId">Optional ID of the profile that initiated the update.</param>
public sealed record UpdateStrategyExecutionArgs(
    UpdateStrategy Strategy,
    IReadOnlyList<ContentManifest> OldManifests,
    IReadOnlyList<ContentManifest> NewManifests,
    IReadOnlyDictionary<string, string> ManifestMapping,
    string LatestVersion,
    bool ShouldDeleteOldVersions,
    string? TriggeringProfileId);
