namespace GenHub.Core.Models.CommunityOutpost;

/// <summary>
/// Information about an entry in a .big archive.
/// </summary>
/// <param name="RelativePath">Relative path within archive.</param>
/// <param name="Offset">Byte offset of entry payload.</param>
/// <param name="Size">Byte length of entry payload.</param>
public sealed record BigArchiveEntryInfo(string RelativePath, uint Offset, uint Size);
