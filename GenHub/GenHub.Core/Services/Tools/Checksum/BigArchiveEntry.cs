namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Represents a single file entry inside a SAGE .BIG archive.
/// </summary>
/// <param name="Path">The relative file path inside the archive.</param>
/// <param name="ArchivePath">The absolute path of the containing .BIG archive.</param>
/// <param name="Offset">The byte offset of the entry data inside the archive.</param>
/// <param name="Size">The byte size of the entry data.</param>
public sealed record BigArchiveEntry(string Path, string ArchivePath, int Offset, int Size);
