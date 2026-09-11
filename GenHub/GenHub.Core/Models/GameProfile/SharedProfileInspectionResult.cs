using System.Collections.Generic;
using GenHub.Core.Models.GameInstallations;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Pre-import inspection result detailing compatibility, missing downloads, and security validation.
/// </summary>
public sealed class SharedProfileInspectionResult
{
    /// <summary>
    /// Gets the profile metadata.
    /// </summary>
    public required SharedProfileMetadata ProfileMetadata { get; init; }

    /// <summary>
    /// Gets the itemized manifest dependencies and their local cache status.
    /// </summary>
    public required IReadOnlyList<SharedManifestDependency> Manifests { get; init; }

    /// <summary>
    /// Gets a value indicating whether a compatible local game installation was found.
    /// </summary>
    public required bool HasValidGameInstallation { get; init; }

    /// <summary>
    /// Gets the automatically matched game installation ID if available.
    /// </summary>
    public required string? MatchedGameInstallationId { get; init; }

    /// <summary>
    /// Gets the list of compatible installations available on this machine.
    /// </summary>
    public required IReadOnlyList<GameInstallation> CompatibleInstallations { get; init; }

    /// <summary>
    /// Gets the total bytes required to download missing dependencies.
    /// </summary>
    public required long TotalDownloadBytesRequired { get; init; }

    /// <summary>
    /// Gets the count of manifests already cached locally.
    /// </summary>
    public required int CachedManifestCount { get; init; }

    /// <summary>
    /// Gets the count of manifests requiring download.
    /// </summary>
    public required int MissingManifestCount { get; init; }

    /// <summary>
    /// Gets a value indicating whether an existing profile shares this name.
    /// </summary>
    public required bool HasNameConflict { get; init; }

    /// <summary>
    /// Gets the suggested unique profile name.
    /// </summary>
    public required string SuggestedProfileName { get; init; }

    /// <summary>
    /// Gets the list of security warnings or sanitization alerts.
    /// </summary>
    public required IReadOnlyList<string> SecurityWarnings { get; init; }

    /// <summary>
    /// Gets the underlying shared package.
    /// </summary>
    public required SharedGameProfilePackage Package { get; init; }
}
