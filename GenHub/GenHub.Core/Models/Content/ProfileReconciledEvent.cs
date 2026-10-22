using System.Collections.Generic;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Event raised when a profile is updated during reconciliation.
/// </summary>
public record ProfileReconciledEvent(
    string ProfileId,
    string ProfileName,
    IReadOnlyList<string> OldManifestIds,
    IReadOnlyList<string> NewManifestIds);
