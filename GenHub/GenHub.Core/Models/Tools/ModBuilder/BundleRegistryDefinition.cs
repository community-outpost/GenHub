using System.Text;
using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents a file hash registry definition for change detection optimization.
/// </summary>
public class BundleRegistryDefinition
{
    /// <summary>
    /// Gets or sets the list of registry file paths.
    /// </summary>
    [JsonPropertyName("paths")]
    public List<string> Paths { get; set; } = new();

    /// <summary>
    /// Gets or sets the CRC32 checksum of all registry paths combined.
    /// </summary>
    [JsonPropertyName("crc32")]
    public uint Crc32 { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BundleRegistryDefinition"/> class.
    /// </summary>
    public BundleRegistryDefinition()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BundleRegistryDefinition"/> class with paths.
    /// </summary>
    /// <param name="paths">The registry file paths.</param>
    public BundleRegistryDefinition(List<string> paths)
    {
        Paths = paths ?? new List<string>();
        if (Paths.Count > 0)
        {
            Crc32 = CalculateCrc32();
        }
    }

    /// <summary>
    /// Calculates the CRC32 checksum of all paths combined.
    /// </summary>
    /// <returns>The CRC32 checksum value.</returns>
    private uint CalculateCrc32()
    {
        var pathsStr = string.Join(string.Empty, Paths);
        var pathsBytes = Encoding.UTF8.GetBytes(pathsStr);

        // Simple CRC32 implementation
        uint crc = 0xFFFFFFFF;
        foreach (var b in pathsBytes)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }
}
