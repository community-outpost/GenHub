using System.Collections.Generic;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Represents a content manifest dependency referenced by a shared profile.
/// </summary>
public sealed class SharedManifestDependency
{
    /// <summary>
    /// Gets the unique identifier of the manifest.
    /// </summary>
    public required string ManifestId { get; init; }

    /// <summary>
    /// Gets the user-friendly display name of the content.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the version string.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the type of content (Mod, Patch, MapPack, etc.).
    /// </summary>
    public required ContentType ContentType { get; init; }

    /// <summary>
    /// Gets the target game type (Generals, ZeroHour, or Unknown).
    /// </summary>
    public GameType TargetGame { get; init; } = GameType.Unknown;

    /// <summary>
    /// Gets the publisher name.
    /// </summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// Gets the publisher type string (e.g. generalsonline, thesuperhackers).
    /// </summary>
    public string? PublisherType { get; init; }

    /// <summary>
    /// Gets the download size in bytes.
    /// </summary>
    public long DownloadSize { get; init; }

    /// <summary>
    /// Gets a value indicating whether this manifest is cached in the local CAS pool.
    /// </summary>
    public bool IsCachedLocally { get; init; }

    /// <summary>
    /// Gets the cryptographic hash of the content package if available.
    /// </summary>
    public string? Hash { get; init; }

    /// <summary>
    /// Gets the direct archive package URL if the component is hosted as a single archive.
    /// </summary>
    public string? PackageUrl { get; init; }

    /// <summary>
    /// Gets the cryptographic hash of the single package archive if applicable.
    /// </summary>
    public string? PackageHash { get; init; }

    /// <summary>
    /// Gets the file entries for downloading this content.
    /// </summary>
    public IReadOnlyList<ManifestFile> Files { get; init; } = [];
}
