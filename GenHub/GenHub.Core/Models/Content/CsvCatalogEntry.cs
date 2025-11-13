using CsvHelper.Configuration.Attributes;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents a single entry in the CSV catalog for game installation files.
/// </summary>
public sealed class CsvCatalogEntry
{
    /// <summary>
    /// Gets or sets the relative path of the file from the game installation root.
    /// </summary>
    [Name("relativePath")]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the size of the file in bytes.
    /// </summary>
    [Name("size")]
    public long Size { get; set; }

    /// <summary>
    /// Gets or sets the MD5 hash of the file.
    /// </summary>
    [Name("md5")]
    public string Md5 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA256 hash of the file.
    /// </summary>
    [Name("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game type (Generals or ZeroHour).
    /// </summary>
    [Name("gameType")]
    public string GameType { get; set; } = "Generals";

    /// <summary>
    /// Gets or sets the language code for the file.
    /// </summary>
    [Name("language")]
    public string Language { get; set; } = "All";

    /// <summary>
    /// Gets or sets a value indicating whether the file is required for validation.
    /// </summary>
    [Name("isRequired")]
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets additional metadata for the file as JSON string.
    /// </summary>
    [Name("metadata")]
    public string? Metadata { get; set; }
}
