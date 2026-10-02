namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// A parsed chunk: label, version, and raw payload bytes.
/// </summary>
public sealed class MapChunkNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MapChunkNode"/> class.
    /// </summary>
    /// <param name="label">Chunk label resolved through the table of contents.</param>
    /// <param name="version">Chunk version.</param>
    /// <param name="data">Raw payload bytes.</param>
    public MapChunkNode(string label, ushort version, byte[] data)
    {
        Label = label;
        Version = version;
        Data = data;
    }

    /// <summary>Gets the chunk label.</summary>
    public string Label { get; }

    /// <summary>Gets the chunk version.</summary>
    public ushort Version { get; }

    /// <summary>Gets the raw payload bytes.</summary>
    public IList<byte> Data { get; }
}
