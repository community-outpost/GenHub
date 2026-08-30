using System;
using System.Collections.Generic;
using GenHub.Core.Constants;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Container package representing a shared game profile and its required content manifests.
/// </summary>
public sealed class SharedGameProfilePackage
{
    /// <summary>
    /// Gets the schema version of the package format.
    /// </summary>
    public int SchemaVersion { get; init; } = ProfileSharingConstants.DefaultSchemaVersion;

    /// <summary>
    /// Gets the generator version of GenHub that created this package.
    /// </summary>
    public string GeneratorVersion { get; init; } = string.Empty;

    /// <summary>
    /// Gets the date and time when this profile package was exported in UTC.
    /// </summary>
    public DateTime ExportedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the profile metadata and settings overrides.
    /// </summary>
    public required SharedProfileMetadata Profile { get; init; }

    /// <summary>
    /// Gets the list of required content manifests needed to run this profile.
    /// </summary>
    public required IReadOnlyList<SharedManifestDependency> RequiredManifests { get; init; }
}
