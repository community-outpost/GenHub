using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents layout metadata of a publisher's .BIG archive for byte-for-byte exact reproduction.
/// </summary>
public class BigArchiveManifest
{
    /// <summary>
    /// Gets or sets the target .BIG filename (e.g. "500_900_CommunityPatch_CoreINI.big").
    /// </summary>
    [JsonPropertyName("bigFileName")]
    public string? BigFileName { get; set; }

    /// <summary>
    /// Gets or sets the 8-byte trailer in uppercase hex (e.g. "0000000000000000" or "4C32323500000000").
    /// </summary>
    [JsonPropertyName("trailerHex")]
    public string? TrailerHex { get; set; }

    /// <summary>
    /// Gets or sets an optional header size override when original archive had special alignment/padding.
    /// </summary>
    [JsonPropertyName("headerSizeOverride")]
    public uint? HeaderSizeOverride { get; set; }

    /// <summary>
    /// Gets or sets the original archive's SHA256 hash for byte-for-byte verification.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    /// <summary>
    /// Gets or sets the ordered list of entries with their exact relative paths and casing.
    /// </summary>
    [JsonPropertyName("entryOrder")]
    public List<string> EntryOrder { get; set; } = new();
}
