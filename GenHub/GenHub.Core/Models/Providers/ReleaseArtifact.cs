using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// Represents a downloadable file artifact within a release.
/// </summary>
public class ReleaseArtifact
{
    /// <summary>
    /// Gets or sets the artifact filename (e.g., "MyMod-1.0.0.zip").
    /// </summary>
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the direct download URL.
    /// Supports GitHub Releases, ModDB, generic HTTP, Google Drive, Dropbox, etc.
    /// </summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the SHA256 hash for integrity verification.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the MIME type of the artifact.
    /// </summary>
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the primary artifact.
    /// When multiple artifacts exist, the primary one is downloaded by default.
    /// </summary>
    [JsonPropertyName("isPrimary")]
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Gets or sets the variant axis this artifact belongs to (e.g. "resolution", "language",
    /// "game-type"). When two or more artifacts in a release share an axis, the generic catalog
    /// discoverer splits them into sibling cards under one variant group so the user can pick.
    /// Omit for single-artifact or non-variant releases.
    /// </summary>
    [JsonPropertyName("variantAxis")]
    public string? VariantAxis { get; set; }

    /// <summary>
    /// Gets or sets the human-readable label for this artifact's variant (e.g. "1080p",
    /// "1920x1080", "English"). Shown in the card's variant dropdown.
    /// </summary>
    [JsonPropertyName("variant")]
    public string? Variant { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this artifact is the recommended default for its
    /// axis. Exactly one artifact per axis should be marked default; the discoverer selects it as
    /// the initially chosen variant in the dropdown.
    /// </summary>
    [JsonPropertyName("isDefaultVariant")]
    public bool IsDefaultVariant { get; set; }
}
