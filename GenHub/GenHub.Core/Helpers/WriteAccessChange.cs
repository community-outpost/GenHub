namespace GenHub.Core.Helpers;

/// <summary>
/// The permissions an entry had before <see cref="WriteAccessHelper"/> made it writable.
/// </summary>
/// <param name="Path">The file or directory that was changed.</param>
/// <param name="IsDirectory">Whether the entry is a directory.</param>
/// <param name="OriginalAttributes">The Windows attributes before the change.</param>
/// <param name="OriginalMode">The Unix permissions before the change.</param>
public sealed record WriteAccessChange(string Path, bool IsDirectory, FileAttributes OriginalAttributes, UnixFileMode OriginalMode);
