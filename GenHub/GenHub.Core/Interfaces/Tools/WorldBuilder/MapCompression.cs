using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Compression envelope detected on a map file.
/// </summary>
public enum MapCompression
{
    /// <summary>Raw table-of-contents plus chunks.</summary>
    None = 0,

    /// <summary>EA RefPack ("EAR\0") envelope.</summary>
    RefPack = 1,

    /// <summary>ZLib ("ZL1\0".."ZL9\0") envelope.</summary>
    ZLib = 2,

    /// <summary>Detected but unsupported envelope (Nox, BTree, Huff).</summary>
    Unsupported = 3,
}
